#!/usr/bin/env python3
"""収集した使用率が、ホストの実際の値と一致するかを判定する。

これまでSC-06・SC-07は数値を並べて出すだけで「要目視」だった。
目で見る前提の項目は、**見なかったときに黙って通る。**
桁や単位が違っても検知そのものは動いてしまうため、ずれても気づけない。

判定に落として、ずれたら失敗させる。

標準ライブラリだけで動かす(検証VMへ追加の導入を求めないため)。

    python3 compare_usage.py disk     --df <file> --metrics <file>
    python3 compare_usage.py resource --stats <file> --collected <file>
"""

import argparse
import json
import sys

# --- しきい値 -----------------------------------------------------------

# ディスクは丸めの差しか出ないはずなので厳しく見る。
# ずれるとしたら計算式そのものが違う(root予備領域を分母に入れている等)
DISK_TOLERANCE = 1.0

# メモリはページキャッシュの差し引き方で数ポイント動く
MEMORY_TOLERANCE = 5.0

# CPUは測る瞬間で大きく動く。値が取れていることと、
# 負荷をかけた対象が「明らかに動いている」ことだけを見る
CPU_MIN_WHEN_LOADED = 5.0


def fail(message: str) -> None:
    print(f"  NG   {message}")


def ok(message: str) -> None:
    print(f"  OK   {message}")


# --- SC-06 ディスク -----------------------------------------------------


def parse_df(text: str) -> dict[str, float]:
    """`df -P` の出力から mountpoint -> USE% を作る。"""
    found = {}
    for line in text.splitlines()[1:]:
        parts = line.split()
        if len(parts) < 6:
            continue
        percent = parts[4].rstrip("%")
        try:
            found[parts[5]] = float(percent)
        except ValueError:
            continue
    return found


def parse_metrics(text: str) -> dict[str, float]:
    """filesystem-usage.awk の出力(MOUNTPOINT SIZE AVAIL USE%)を読む。"""
    found = {}
    for line in text.splitlines():
        parts = line.split()
        if len(parts) < 4 or parts[0] == "MOUNTPOINT":
            continue
        try:
            found[parts[0]] = float(parts[3].rstrip("%"))
        except ValueError:
            continue
    return found


def compare_disk(df_text: str, metrics_text: str, tolerance: float) -> int:
    actual = parse_df(df_text)
    collected = parse_metrics(metrics_text)

    if not collected:
        fail("node_exporter から使用率を読めませんでした。")
        return 1
    if not actual:
        fail("df の出力を読めませんでした。")
        return 1

    # 双方に出るマウントポイントだけを見る。
    # node_exporter はコンテナ内のバインドマウント(/etc/hostname 等)も返すため
    shared = sorted(set(actual) & set(collected))
    if not shared:
        fail(f"突き合わせられるマウントポイントがありません(df={sorted(actual)})")
        return 1

    problems = 0
    for mount in shared:
        diff = abs(actual[mount] - collected[mount])
        detail = f"{mount}: df={actual[mount]:.2f}% / 収集={collected[mount]:.2f}% (差 {diff:.2f})"
        if diff <= tolerance:
            ok(detail)
        else:
            fail(detail + f" (許容 {tolerance} を超えています)")
            problems += 1

    if problems:
        print("       全容量を分母にしていないか(root予備領域の扱い)を疑ってください。")
    return 1 if problems else 0


# --- SC-07 リソース使用率 -----------------------------------------------


def parse_docker_stats(text: str) -> dict[str, tuple[float, float]]:
    """`docker stats --format '{{.Name}}	{{.CPUPerc}}	{{.MemPerc}}'` を読む。

    既定の表は列位置が環境で変わるため、書式を固定した出力だけを受ける。
    """
    found = {}
    for line in text.splitlines():
        parts = line.rstrip().split("	")
        if len(parts) != 3:
            continue
        try:
            found[parts[0]] = (
                float(parts[1].rstrip("%")), float(parts[2].rstrip("%"))
            )
        except ValueError:
            continue
    return found


def parse_collected(payload: str) -> dict[str, tuple[float | None, float | None]]:
    """収集値(kind=resource のペイロード)から 名前 -> (CPU%, MEM%) を作る。"""
    data = json.loads(payload)
    found = {}
    for container in data.get("containers") or []:
        name = container.get("name")
        if name:
            found[name] = (
                container.get("cpuUsagePercent"),
                container.get("memoryUsagePercent"),
            )
    return found


def compare_resource(
    stats_text: str, collected_json: str, loaded: str | None,
    memory_tolerance: float,
) -> int:
    actual = parse_docker_stats(stats_text)
    collected = parse_collected(collected_json)

    if not collected:
        fail("収集値にコンテナがありません。")
        return 1

    shared = sorted(set(actual) & set(collected))
    if not shared:
        fail(f"突き合わせられるコンテナがありません(stats={sorted(actual)})")
        return 1

    problems = 0
    for name in shared:
        stat_cpu, stat_mem = actual[name]
        got_cpu, got_mem = collected[name]

        if got_mem is None:
            fail(f"{name}: メモリ使用率が収集されていません。")
            problems += 1
        else:
            diff = abs(stat_mem - got_mem)
            detail = f"{name} MEM: stats={stat_mem:.2f}% / 収集={got_mem:.2f}% (差 {diff:.2f})"
            if diff <= memory_tolerance:
                ok(detail)
            else:
                fail(detail + f" (許容 {memory_tolerance} を超えています)")
                print("       ページキャッシュの差し引きを疑ってください。")
                problems += 1

        # CPUは瞬間値で、収集した時刻とstatsを取った時刻がずれる。
        # 差で判定すると常に落ちるため、取れていることだけを見る
        if got_cpu is None:
            fail(f"{name} CPU: 収集されていません。")
            problems += 1
        else:
            ok(f"{name} CPU: stats={stat_cpu:.2f}% / 収集={got_cpu:.2f}%(瞬間値のため差は見ない)")

    # 負荷をかけた対象が本当に上がっているか。
    # ここを見ないと「常に0%を返す」壊れ方を通してしまう
    if loaded:
        got_cpu, _ = collected.get(loaded, (None, None))
        if got_cpu is None:
            fail(f"{loaded}: 負荷をかけた対象のCPU使用率が収集されていません。")
            problems += 1
        elif got_cpu < CPU_MIN_WHEN_LOADED:
            fail(
                f"{loaded}: 負荷をかけたのにCPUが {got_cpu:.2f}% しかありません"
                f"(期待: {CPU_MIN_WHEN_LOADED}% 以上)"
            )
            problems += 1
        else:
            ok(f"{loaded}: 負荷が使用率に現れている({got_cpu:.2f}%)")

    return 1 if problems else 0


# --- 入口 ---------------------------------------------------------------


def read(path: str) -> str:
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def main() -> int:
    parser = argparse.ArgumentParser(description="収集した使用率を実際の値と突き合わせる")
    sub = parser.add_subparsers(dest="mode", required=True)

    disk = sub.add_parser("disk", help="SC-06: df と node_exporter")
    disk.add_argument("--df", required=True)
    disk.add_argument("--metrics", required=True)
    disk.add_argument("--tolerance", type=float, default=DISK_TOLERANCE)

    resource = sub.add_parser("resource", help="SC-07: docker stats と収集値")
    resource.add_argument("--stats", required=True)
    resource.add_argument("--collected", required=True)
    resource.add_argument("--loaded", help="負荷をかけたコンテナ名")
    resource.add_argument("--memory-tolerance", type=float, default=MEMORY_TOLERANCE)

    args = parser.parse_args()

    if args.mode == "disk":
        return compare_disk(read(args.df), read(args.metrics), args.tolerance)
    return compare_resource(
        read(args.stats), read(args.collected), args.loaded, args.memory_tolerance
    )


if __name__ == "__main__":
    sys.exit(main())
