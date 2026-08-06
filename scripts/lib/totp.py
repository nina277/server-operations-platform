#!/usr/bin/env python3
"""Base32のシークレットからTOTPコードを作る(RFC 6238)。

検証を自動化するために置いている。**検証専用VMでのみ使うこと。**
本番の管理者アカウントのシークレットをここへ渡してはならない。

標準ライブラリだけで動かす。検証VMへ追加の導入を求めないため。

    python3 totp.py <base32-secret> [--digits 6] [--period 30] [--at <unixtime>]
"""

import argparse
import base64
import hashlib
import hmac
import struct
import sys
import time


def totp(secret: str, *, digits: int = 6, period: int = 30, at: float | None = None) -> str:
    # 認証アプリが表示するのと同じ形(空白・ハイフンを許し、大文字へ、パディングを補う)
    normalized = secret.strip().replace(" ", "").replace("-", "").upper()
    normalized += "=" * (-len(normalized) % 8)
    key = base64.b32decode(normalized, casefold=True)

    counter = int((time.time() if at is None else at) // period)
    digest = hmac.new(key, struct.pack(">Q", counter), hashlib.sha1).digest()

    # 動的切り捨て(RFC 4226 5.4)
    offset = digest[-1] & 0x0F
    code = struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF
    return str(code % (10 ** digits)).zfill(digits)


def main() -> int:
    parser = argparse.ArgumentParser(description="TOTPコードを出力する")
    parser.add_argument("secret", help="Base32のシークレット")
    parser.add_argument("--digits", type=int, default=6)
    parser.add_argument("--period", type=int, default=30)
    parser.add_argument("--at", type=float, default=None, help="この時刻(UNIX秒)で計算する")
    parser.add_argument(
        "--wait-fresh",
        action="store_true",
        help="残り時間が短いときは次の窓まで待つ。API側へ届く前に切り替わるのを防ぐ",
    )
    args = parser.parse_args()

    if args.wait_fresh and args.at is None:
        remaining = args.period - (time.time() % args.period)
        if remaining < 5:
            time.sleep(remaining + 0.5)

    print(totp(args.secret, digits=args.digits, period=args.period, at=args.at))
    return 0


if __name__ == "__main__":
    sys.exit(main())
