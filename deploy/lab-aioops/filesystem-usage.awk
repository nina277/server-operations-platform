# node_exporter の出力からファイルシステム使用率を計算して表に出す。
#
# 目的は「監視システムが出す値と同じものを、その場で手計算できるようにする」こと。
# 計算式は HostMetricsAdapter とそろえてある。ここと画面の値がずれたら、
# どちらかが間違っている。
#
#   使用量 = 全容量 − 空き容量(free)
#   使用率 = 使用量 ÷ (使用量 + 利用可能量(avail))
#
# 全容量を分母にしないのは、ext4などがroot専用の予備領域を持つため。
# 分母を全容量にすると、一般利用者が書けなくなっても余裕があるように見える。

function label(line, name,   start, len) {
    if (!match(line, name "=\"[^\"]*\"")) {
        return ""
    }

    # name=" の分を飛ばし、末尾の " を落とす
    start = RSTART + length(name) + 2
    len = RLENGTH - length(name) - 3
    return substr(line, start, len)
}

function value(line,   tail, parts) {
    tail = line
    sub(/^[^}]*\} */, "", tail)
    split(tail, parts, " ")
    return parts[1] + 0
}

function pseudo(fstype) {
    return fstype == "tmpfs" || fstype == "devtmpfs" || fstype == "overlay" ||
           fstype == "squashfs" || fstype == "ramfs" || fstype == "rootfs" ||
           fstype == "autofs" || fstype == "nsfs" || fstype ~ /^fuse\./
}

/^node_filesystem_(size|avail|free)_bytes\{/ {
    fstype = label($0, "fstype")
    mount = label($0, "mountpoint")
    if (mount == "" || pseudo(fstype)) {
        next
    }

    if ($0 ~ /^node_filesystem_size_bytes/)  { size[mount] = value($0); seen[mount] = 1 }
    if ($0 ~ /^node_filesystem_avail_bytes/) { avail[mount] = value($0) }
    if ($0 ~ /^node_filesystem_free_bytes/)  { free[mount] = value($0); hasfree[mount] = 1 }
}

END {
    printf "%-28s %12s %12s %8s\n", "MOUNTPOINT", "SIZE", "AVAIL", "USE%"

    for (mount in seen) {
        if (size[mount] <= 0 || !(mount in avail)) {
            # 片方しか無いものは出さない。0%として出すと空きがあるように見える
            continue
        }

        used = hasfree[mount] ? size[mount] - free[mount] : size[mount] - avail[mount]
        denominator = used + avail[mount]
        if (used < 0 || denominator <= 0) {
            continue
        }

        printf "%-28s %12d %12d %7.2f%%\n",
            mount, size[mount], avail[mount], used / denominator * 100
    }
}
