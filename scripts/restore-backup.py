#!/usr/bin/env python3
"""バックアップを取り出して復号する。

このシステムのバックアップは AES-256-GCM で暗号化してS3互換の保存先へ置く。
**画面にもAPIにも取り出す口が無いため、これが唯一の復元手段になる。**

バックアップは「取れていること」を確かめても意味がなく、
**戻せることを確かめて初めて価値がある。** 定期的にこれを通すこと。

標準ライブラリだけで動かす。復元が必要な場面で追加の導入を求めないため
(壊れたあとにpipが通る保証は無い)。

使い方:

  # 保存先から取り出して復号する
  ./scripts/restore-backup.py \\
      --endpoint http://minio:9000 --bucket serverops-backup \\
      --object server-operations/backup-20260808-033645.bin \\
      --access-key KEY --secret-key SECRET \\
      --encryption-key "$BACKUP_ENCRYPTION_KEY"

  # 保存先の一覧を見る
  ./scripts/restore-backup.py --endpoint ... --bucket ... \\
      --access-key ... --secret-key ... --list

  # 手元にある暗号化ファイルを復号する
  ./scripts/restore-backup.py --file backup.bin --encryption-key "..."

鍵はコマンドラインに書かず、環境変数でも渡せる。
psで見えるため、共用の機械では環境変数を使うこと。

  BACKUP_ACCESS_KEY / BACKUP_SECRET_KEY / BACKUP_ENCRYPTION_KEY

出力はJSONのスナップショット。次を含む。

  利用者 / 設定 / 許可ネットワーク / 監視対象 / 対象プロファイル / 診断ルール

**含まないもの: 暗号化済みの秘密値と、収集した実データ。**
秘密値はData Protectionの鍵に依存するため、鍵を別に保全する運用になっている。
つまりこれを戻しても、秘密情報(SMTPパスワード等)は入れ直しが要る。
"""

import argparse
import datetime
import hashlib
import hmac
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from typing import Any

# BackupService.Encrypt の出力形式に合わせる。
# ここがずれると復号できない。C#側は BackupFormatTests が固定している。
NONCE_SIZE = 12
TAG_SIZE = 16


# --- 復号 ---------------------------------------------------------------


def decrypt(blob: bytes, key: str) -> bytes:
    """nonce(12) + tag(16) + 暗号文 を復号する。鍵は SHA-256 で導出する。"""
    if len(blob) < NONCE_SIZE + TAG_SIZE:
        raise ValueError(
            f"暗号化データが短すぎます({len(blob)}バイト)。"
            "取得に失敗しているか、別の形式のファイルです。"
        )

    derived = hashlib.sha256(key.encode("utf-8")).digest()
    nonce = blob[:NONCE_SIZE]
    tag = blob[NONCE_SIZE : NONCE_SIZE + TAG_SIZE]
    ciphertext = blob[NONCE_SIZE + TAG_SIZE :]

    return _aes_gcm_decrypt(derived, nonce, ciphertext, tag)


def _aes_gcm_decrypt(key: bytes, nonce: bytes, ciphertext: bytes, tag: bytes) -> bytes:
    """AES-GCMの復号。標準ライブラリにAESが無いため自前で持つ。

    復号が必要なのは壊れたあとで、そのときにpip installが通るとは限らない。
    ここだけのために依存を増やさない。
    """
    expanded = _aes_expand_key(key)

    # GCM: H = E(K, 0^128)、J0 = nonce || 0^31 || 1 (nonceが96bitのとき)
    h = _aes_encrypt_block(bytes(16), expanded)
    j0 = nonce + b"\x00\x00\x00\x01"

    # 認証タグを先に検証する。改竄された入力を復号して返さない
    expected = _ghash(h, b"", ciphertext)
    expected = bytes(
        a ^ b for a, b in zip(expected, _aes_encrypt_block(j0, expanded))
    )
    if not hmac.compare_digest(expected, tag):
        raise ValueError(
            "認証タグが一致しません。"
            "**暗号化キーが違うか、データが壊れています。**\n"
            "  鍵を変更した後に取ったバックアップでないか確認してください。"
        )

    return _gctr(expanded, _inc32(j0), ciphertext)


def _inc32(block: bytes) -> bytes:
    counter = (int.from_bytes(block[12:], "big") + 1) & 0xFFFFFFFF
    return block[:12] + counter.to_bytes(4, "big")


def _gctr(expanded: list[list[int]], counter: bytes, data: bytes) -> bytes:
    out = bytearray()
    for offset in range(0, len(data), 16):
        chunk = data[offset : offset + 16]
        stream = _aes_encrypt_block(counter, expanded)
        out += bytes(a ^ b for a, b in zip(chunk, stream))
        counter = _inc32(counter)
    return bytes(out)


def _ghash(h: bytes, aad: bytes, ciphertext: bytes) -> bytes:
    def mul(x: int, y: int) -> int:
        # GF(2^128) の乗算。既約多項式は x^128 + x^7 + x^2 + x + 1
        z = 0
        for i in range(128):
            if y & (1 << (127 - i)):
                z ^= x
            if x & 1:
                x = (x >> 1) ^ (0xE1 << 120)
            else:
                x >>= 1
        return z

    hi = int.from_bytes(h, "big")
    acc = 0
    for data in (aad, ciphertext):
        for offset in range(0, len(data), 16):
            block = data[offset : offset + 16].ljust(16, b"\x00")
            acc = mul(acc ^ int.from_bytes(block, "big"), hi)

    lengths = (len(aad) * 8).to_bytes(8, "big") + (len(ciphertext) * 8).to_bytes(8, "big")
    acc = mul(acc ^ int.from_bytes(lengths, "big"), hi)
    return acc.to_bytes(16, "big")


# --- AES(復号には暗号化方向だけあればよい。GCMはCTRで動くため) ----------

_SBOX: list[int] = []
_RCON = [0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1B, 0x36, 0x6C, 0xD8, 0xAB, 0x4D]


def _build_sbox() -> None:
    if _SBOX:
        return
    p = q = 1
    sbox = [0] * 256
    while True:
        # pを生成元3で進め、qはその逆元を辿る
        p = p ^ ((p << 1) & 0xFF) ^ (0x1B if p & 0x80 else 0)
        q ^= q << 1
        q ^= q << 2
        q ^= q << 4
        q &= 0xFF
        if q & 0x80:
            q ^= 0x09
        value = q ^ ((q << 1) | (q >> 7)) ^ ((q << 2) | (q >> 6)) \
            ^ ((q << 3) | (q >> 5)) ^ ((q << 4) | (q >> 4))
        sbox[p] = (value ^ 0x63) & 0xFF
        if p == 1:
            break
    sbox[0] = 0x63
    _SBOX.extend(sbox)


def _xtime(a: int) -> int:
    a <<= 1
    return (a ^ 0x1B) & 0xFF if a & 0x100 else a


def _aes_expand_key(key: bytes) -> list[list[int]]:
    _build_sbox()
    nk = len(key) // 4
    rounds = nk + 6
    words = [list(key[4 * i : 4 * i + 4]) for i in range(nk)]

    for i in range(nk, 4 * (rounds + 1)):
        temp = list(words[i - 1])
        if i % nk == 0:
            temp = temp[1:] + temp[:1]
            temp = [_SBOX[b] for b in temp]
            temp[0] ^= _RCON[i // nk - 1]
        elif nk > 6 and i % nk == 4:
            temp = [_SBOX[b] for b in temp]
        words.append([words[i - nk][j] ^ temp[j] for j in range(4)])

    return [sum(words[4 * r : 4 * r + 4], []) for r in range(rounds + 1)]


def _aes_encrypt_block(block: bytes, round_keys: list[list[int]]) -> bytes:
    state = [b ^ k for b, k in zip(block, round_keys[0])]
    rounds = len(round_keys) - 1

    for rnd in range(1, rounds + 1):
        state = [_SBOX[b] for b in state]
        # ShiftRows(列優先で並んでいるため添字で回す)
        state = [state[(i + 4 * (i % 4)) % 16] for i in range(16)]

        if rnd != rounds:
            mixed = []
            for c in range(4):
                col = state[4 * c : 4 * c + 4]
                t = col[0] ^ col[1] ^ col[2] ^ col[3]
                mixed += [
                    col[i] ^ t ^ _xtime(col[i] ^ col[(i + 1) % 4]) for i in range(4)
                ]
            state = mixed

        state = [b ^ k for b, k in zip(state, round_keys[rnd])]

    return bytes(state)


# --- S3(SigV4を自前で署名する。boto3を要求しない) -----------------------


def _sign(key: bytes, message: str) -> bytes:
    return hmac.new(key, message.encode("utf-8"), hashlib.sha256).digest()


def s3_request(
    *, endpoint: str, bucket: str, key: str, access_key: str, secret_key: str,
    region: str, query: dict[str, str] | None = None,
) -> bytes:
    """SigV4で署名してGETする。MinIO想定でパス形式を使う。"""
    parsed = urllib.parse.urlparse(endpoint)
    host = parsed.netloc
    path = f"/{bucket}" + (f"/{key}" if key else "")
    canonical_uri = urllib.parse.quote(path, safe="/~")

    query = query or {}
    canonical_query = "&".join(
        f"{urllib.parse.quote(k, safe='~')}={urllib.parse.quote(v, safe='~')}"
        for k, v in sorted(query.items())
    )

    now = datetime.datetime.now(datetime.timezone.utc)
    amz_date = now.strftime("%Y%m%dT%H%M%SZ")
    date_stamp = now.strftime("%Y%m%d")
    payload_hash = hashlib.sha256(b"").hexdigest()

    canonical_headers = f"host:{host}\nx-amz-content-sha256:{payload_hash}\nx-amz-date:{amz_date}\n"
    signed_headers = "host;x-amz-content-sha256;x-amz-date"
    canonical_request = "\n".join(
        ["GET", canonical_uri, canonical_query, canonical_headers, signed_headers, payload_hash]
    )

    scope = f"{date_stamp}/{region}/s3/aws4_request"
    string_to_sign = "\n".join(
        [
            "AWS4-HMAC-SHA256",
            amz_date,
            scope,
            hashlib.sha256(canonical_request.encode("utf-8")).hexdigest(),
        ]
    )

    signing_key = _sign(
        _sign(_sign(_sign(f"AWS4{secret_key}".encode("utf-8"), date_stamp), region), "s3"),
        "aws4_request",
    )
    signature = hmac.new(
        signing_key, string_to_sign.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    url = f"{endpoint.rstrip('/')}{canonical_uri}"
    if canonical_query:
        url += f"?{canonical_query}"

    request = urllib.request.Request(url, method="GET")
    request.add_header("Host", host)
    request.add_header("x-amz-date", amz_date)
    request.add_header("x-amz-content-sha256", payload_hash)
    request.add_header(
        "Authorization",
        f"AWS4-HMAC-SHA256 Credential={access_key}/{scope}, "
        f"SignedHeaders={signed_headers}, Signature={signature}",
    )

    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", "replace")[:400]
        raise SystemExit(
            f"保存先から取得できません(HTTP {error.code})。\n  {body}"
        ) from error
    except urllib.error.URLError as error:
        raise SystemExit(f"保存先へ接続できません: {error.reason}") from error


def list_objects(**kwargs: Any) -> list[tuple[str, str, str]]:
    body = s3_request(key="", query={"list-type": "2"}, **kwargs)
    root = ET.fromstring(body)
    namespace = {"s3": root.tag.split("}")[0].strip("{")} if "}" in root.tag else {}
    prefix = "s3:" if namespace else ""
    found = []
    for item in root.findall(f"{prefix}Contents", namespace):
        def text(tag: str) -> str:
            node = item.find(f"{prefix}{tag}", namespace)
            return node.text or "" if node is not None else ""

        found.append((text("Key"), text("LastModified"), text("Size")))
    return sorted(found, key=lambda row: row[1], reverse=True)


# --- 入口 ---------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(
        description="バックアップを取り出して復号する",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--file", help="手元にある暗号化ファイル(保存先へ行かない)")
    parser.add_argument("--endpoint", help="S3互換の保存先。例: http://minio:9000")
    parser.add_argument("--bucket")
    parser.add_argument("--object", help="取り出すオブジェクトキー")
    parser.add_argument("--region", default="us-east-1")
    parser.add_argument("--access-key", default=os.environ.get("BACKUP_ACCESS_KEY"))
    parser.add_argument("--secret-key", default=os.environ.get("BACKUP_SECRET_KEY"))
    parser.add_argument(
        "--encryption-key", default=os.environ.get("BACKUP_ENCRYPTION_KEY")
    )
    parser.add_argument("--list", action="store_true", help="保存先の一覧を出す")
    parser.add_argument("-o", "--output", help="書き出す先(既定: 標準出力)")
    args = parser.parse_args()

    if args.list:
        for name in ("endpoint", "bucket", "access_key", "secret_key"):
            if not getattr(args, name):
                parser.error(f"--list には --{name.replace('_', '-')} が要ります")
        rows = list_objects(
            endpoint=args.endpoint, bucket=args.bucket, access_key=args.access_key,
            secret_key=args.secret_key, region=args.region,
        )
        if not rows:
            print("保存先にオブジェクトがありません。", file=sys.stderr)
            return 1
        print(f"{'更新日時':<26} {'サイズ':>10}  キー")
        for key, modified, size in rows:
            print(f"{modified:<26} {size:>10}  {key}")
        return 0

    if not args.encryption_key:
        parser.error("--encryption-key(または BACKUP_ENCRYPTION_KEY)が要ります")

    if args.file:
        with open(args.file, "rb") as handle:
            blob = handle.read()
    elif args.object:
        for name in ("endpoint", "bucket", "access_key", "secret_key"):
            if not getattr(args, name):
                parser.error(f"取り出しには --{name.replace('_', '-')} が要ります")
        blob = s3_request(
            endpoint=args.endpoint, bucket=args.bucket, key=args.object,
            access_key=args.access_key, secret_key=args.secret_key, region=args.region,
        )
    else:
        parser.error("--file か --object のどちらかを指定してください")

    try:
        plaintext = decrypt(blob, args.encryption_key)
    except ValueError as error:
        print(f"復号できません: {error}", file=sys.stderr)
        return 1

    # 読める形に整えてから出す。中身を目で確かめられないと復元の役に立たない
    try:
        formatted = json.dumps(
            json.loads(plaintext.decode("utf-8")), ensure_ascii=False, indent=2
        )
    except (UnicodeDecodeError, json.JSONDecodeError):
        print(
            "復号はできましたが、中身がJSONとして読めません。そのまま書き出します。",
            file=sys.stderr,
        )
        formatted = None

    if formatted is None:
        data = plaintext
        if args.output:
            with open(args.output, "wb") as handle:
                handle.write(data)
        else:
            sys.stdout.buffer.write(data)
    else:
        if args.output:
            with open(args.output, "w", encoding="utf-8") as handle:
                handle.write(formatted + "\n")
        else:
            print(formatted)

    if args.output:
        print(f"書き出しました: {args.output}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
