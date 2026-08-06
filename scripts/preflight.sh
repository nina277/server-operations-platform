#!/usr/bin/env bash
#
# 配置先の機械が要件を満たしているかを確認する。
#
# **配置する機械の上で実行すること。**開発機で実行しても意味がない。
# 何も変更しない。読むだけ。
#
# 使い方:
#   ./scripts/preflight.sh            本番として確認する
#   ./scripts/preflight.sh --lab      検証環境として確認する(使うポートが増える)
#
# 終了コード: 0 = 配置してよい / 1 = 直すべき問題がある

set -uo pipefail

MODE="production"
[ "${1:-}" = "--lab" ] && MODE="lab"

HTTP_PORT="${HTTP_PORT:-8080}"

problems=0
warnings=0

ok()   { printf '  \033[32mOK\033[0m   %s\n' "$*"; }
warn() { printf '  \033[33m注意\033[0m %s\n' "$*"; warnings=$((warnings + 1)); }
ng()   { printf '  \033[31mNG\033[0m   %s\n' "$*"; problems=$((problems + 1)); }
head2() { printf '\n\033[1m%s\033[0m\n' "$*"; }

# --- 道具 ---------------------------------------------------------------

head2 "必要な道具"

for tool in docker curl openssl git; do
  if command -v "${tool}" >/dev/null 2>&1; then
    ok "${tool} がある"
  else
    ng "${tool} が無い"
  fi
done

if command -v python3 >/dev/null 2>&1; then
  ok "python3 がある($(python3 --version 2>&1))"
else
  if [ "${MODE}" = "lab" ]; then
    ng "python3 が無い(lab-verify.sh に必要)"
  else
    warn "python3 が無い(配置には不要。検証を自動化するなら要る)"
  fi
fi

if docker compose version >/dev/null 2>&1; then
  ok "docker compose v2 が使える($(docker compose version --short 2>/dev/null))"
else
  ng "docker compose v2 が使えない(docker-compose v1 では動かない)"
fi

if docker info >/dev/null 2>&1; then
  ok "Dockerデーモンへ接続できる"
else
  ng "Dockerデーモンへ接続できない(起動していないか、利用者が docker グループに入っていない)"
fi

# --- 機械の性能 ---------------------------------------------------------

head2 "機械の性能"

arch="$(uname -m)"
case "${arch}" in
  x86_64|aarch64|arm64) ok "CPUアーキテクチャ: ${arch}" ;;
  *) warn "CPUアーキテクチャ: ${arch}(使うイメージが対応していない可能性がある)" ;;
esac

if [ -r /proc/meminfo ]; then
  mem_mb=$(( $(awk '/^MemTotal:/ {print $2}' /proc/meminfo) / 1024 ))
  # api 512m + worker 512m + mysql 1g + web 128m + nginx 64m にホストの分を足す
  if [ "${mem_mb}" -ge 3500 ]; then
    ok "メモリ: ${mem_mb} MB"
  elif [ "${mem_mb}" -ge 2500 ]; then
    warn "メモリ: ${mem_mb} MB(Composeの上限合計は約2.2GB。余裕が少ない)"
  else
    ng "メモリ: ${mem_mb} MB(Composeの上限合計 約2.2GB に足りない)"
  fi
fi

avail_gb=$(df -P . 2>/dev/null | awk 'NR==2 {printf "%d", $4/1024/1024}')
if [ -n "${avail_gb}" ]; then
  if [ "${avail_gb}" -ge 20 ]; then
    ok "空きディスク: ${avail_gb} GB"
  elif [ "${avail_gb}" -ge 10 ]; then
    warn "空きディスク: ${avail_gb} GB(イメージのビルドと収集値の蓄積で増える)"
  else
    ng "空きディスク: ${avail_gb} GB(イメージのビルドだけで数GB使う)"
  fi
fi

# --- 時刻 ---------------------------------------------------------------

head2 "時刻"

# MFAはTOTPを使う。時計がずれていると**正しいコードでもログインできない**。
# 管理操作はすべてMFAの再認証を要求するため、ここがずれると何もできなくなる。
synced="unknown"
if command -v timedatectl >/dev/null 2>&1; then
  if timedatectl show -p NTPSynchronized --value 2>/dev/null | grep -q '^yes$'; then
    synced="yes"
  else
    synced="no"
  fi
elif command -v chronyc >/dev/null 2>&1 && chronyc tracking >/dev/null 2>&1; then
  synced="yes"
fi

case "${synced}" in
  yes) ok "時刻が同期されている(MFAのTOTPに必要)" ;;
  no)  ng "時刻が同期されていない。**TOTPが合わずログインできなくなる**" ;;
  *)   warn "時刻の同期状態を判定できない。MFAが失敗する場合はここを疑う" ;;
esac

printf '       いまの時刻: %s\n' "$(date -u '+%Y-%m-%d %H:%M:%S UTC')"

# --- cgroup -------------------------------------------------------------

head2 "cgroup(リソース使用率の収集に関係する)"

if [ -f /sys/fs/cgroup/cgroup.controllers ]; then
  ok "cgroup v2"
elif [ -d /sys/fs/cgroup/memory ]; then
  warn "cgroup v1(メモリ使用率は total_inactive_file で計算する。動作はする)"
else
  warn "cgroupの版を判定できない"
fi

# --- ポート -------------------------------------------------------------

head2 "使うポート"

ports=("${HTTP_PORT}:監視システムのWeb口")
if [ "${MODE}" = "lab" ]; then
  ports+=("2375:Docker Socket Proxy" "18080:lab-web" "18081:lab-api" "19100:node-exporter")
fi

port_in_use() {
  if command -v ss >/dev/null 2>&1; then
    ss -ltn 2>/dev/null | awk '{print $4}' | grep -qE "[:.]$1\$"
  elif command -v netstat >/dev/null 2>&1; then
    netstat -ltn 2>/dev/null | awk '{print $4}' | grep -qE "[:.]$1\$"
  else
    return 1
  fi
}

for entry in "${ports[@]}"; do
  port="${entry%%:*}"
  label="${entry#*:}"
  if port_in_use "${port}"; then
    ng "ポート ${port}(${label})が既に使われている"
  else
    ok "ポート ${port}(${label})が空いている"
  fi
done

# --- 既存の配置との衝突 -------------------------------------------------

head2 "既存の配置との衝突"

if docker ps -a --format '{{.Names}}' 2>/dev/null | grep -q '^server-operations'; then
  warn "server-operations の名前を持つコンテナが既にある(更新なら想定どおり)"
else
  ok "同名のコンテナは無い"
fi

if docker volume ls --format '{{.Name}}' 2>/dev/null | grep -q 'server-operations'; then
  warn "server-operations のボリュームが既にある。**消すと収集値と監査ログが消える**"
else
  ok "同名のボリュームは無い"
fi

# --- 秘密情報 -----------------------------------------------------------

head2 "秘密情報"

env_file="$(dirname "${BASH_SOURCE[0]}")/../deploy/.env"
if [ -f "${env_file}" ]; then
  if grep -q 'dummy-' "${env_file}"; then
    ng ".env にダミー値が残っている。**すべて実際の値へ変えること**"
  else
    ok ".env にダミー値は残っていない"
  fi
  perms="$(stat -c '%a' "${env_file}" 2>/dev/null || stat -f '%A' "${env_file}" 2>/dev/null)"
  if [ "${perms}" = "600" ]; then
    ok ".env の権限が 600"
  else
    warn ".env の権限が ${perms}(600 にすること: chmod 600 deploy/.env)"
  fi
else
  warn ".env が無い(初回配置ならこれから作る: cp deploy/.env.example deploy/.env)"
fi

# --- 外への接続 ---------------------------------------------------------

head2 "イメージの取得"

if timeout 10 docker pull hello-world >/dev/null 2>&1; then
  ok "Docker Hub からイメージを取得できる"
  docker rmi hello-world >/dev/null 2>&1 || true
else
  ng "Docker Hub からイメージを取得できない(初回のビルドに必要)"
fi

# --- まとめ -------------------------------------------------------------

printf '\n'
if [ "${problems}" -gt 0 ]; then
  printf '\033[31m%d 件の問題があります。直してから配置してください。\033[0m\n' "${problems}"
  [ "${warnings}" -gt 0 ] && printf '注意が %d 件あります。\n' "${warnings}"
  exit 1
fi

if [ "${warnings}" -gt 0 ]; then
  printf '\033[33m注意が %d 件あります。内容を確認してから配置してください。\033[0m\n' "${warnings}"
else
  printf '\033[32m配置してよい状態です。\033[0m\n'
fi
exit 0
