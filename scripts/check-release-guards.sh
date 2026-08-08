#!/usr/bin/env bash
#
# リリース時に守るべき条件のうち、機械的に確かめられるものを検査する。
#
# ここで見るのは指示書の禁止事項に直結する項目に絞る。
#   - latestタグだけを使うイメージ運用をしていないこと
#   - DockerソケットをWeb UI・APIコンテナへ直接渡していないこと
#   - .env(実際の秘密値)をGit管理していないこと
#
# 使い方:
#   ./scripts/check-release-guards.sh

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

failures=0

fail() {
  echo "NG: $1" >&2
  failures=$((failures + 1))
}

ok() {
  echo "OK: $1"
}

# --- 1. イメージのタグが固定されていること ---

compose_files=$(find "${ROOT_DIR}/deploy" -name 'docker-compose*.yml')

unpinned=$(grep -Hn '^\s*image:' ${compose_files} \
  | grep -E ':\s*[^ ]+(:latest)?\s*$' \
  | grep -vE 'image:\s*[^ ]+:[^ ]+$' || true)

latest_tags=$(grep -Hn '^\s*image:.*:latest\s*$' ${compose_files} || true)

if [ -n "${unpinned}" ] || [ -n "${latest_tags}" ]; then
  fail "イメージのタグが固定されていません。"
  [ -n "${unpinned}" ] && echo "${unpinned}" >&2
  [ -n "${latest_tags}" ] && echo "${latest_tags}" >&2
else
  ok "すべてのイメージが版を指定しています。"
fi

# --- 2. Dockerソケットをweb/apiへ渡していないこと ---
#
# 本番のComposeでは docker.sock を一切マウントしない。
# 検証環境ではSocket Proxyだけが持つ。

prod_socket=$(grep -n 'docker\.sock' "${ROOT_DIR}/deploy/docker-compose.yml" || true)
if [ -n "${prod_socket}" ]; then
  fail "本番のComposeで docker.sock をマウントしています。"
  echo "${prod_socket}" >&2
else
  ok "本番のComposeは docker.sock をマウントしていません。"
fi

lab_compose="${ROOT_DIR}/deploy/lab-aioops/docker-compose.yml"
if [ -f "${lab_compose}" ]; then
  # docker.sock を持つサービスを列挙し、socket-proxy 以外が持っていないか見る。
  # コメント行は見ない。「docker.sock は渡さない」と書いた説明を
  # マウントと取り違えないため(実際にそれで誤検知した)。
  holder=$(awk '
    /^[[:space:]]*#/ { next }
    /^  [a-zA-Z0-9_-]+:/ { service = $1; sub(":", "", service) }
    /docker\.sock/ { print service }
  ' "${lab_compose}" | sort -u)

  # docker.sock を持ってよいのはプロキシ2本だけ。
  # socket-proxy(監視用・狭い) と deploy-proxy(展開用・広い)。
  unexpected=$(echo "${holder}" | grep -vE '^(socket-proxy|deploy-proxy)$' | grep -v '^$' || true)
  if [ -n "${unexpected}" ]; then
    fail "検証環境でプロキシ以外が docker.sock を持っています: ${unexpected}"
  else
    ok "検証環境で docker.sock を持つのはプロキシ2本だけです。"
  fi

  # **名前を許すだけでは足りない。**権限そのものを確かめる。
  # 二層の境界は「無人で動く経路に展開の権限が無い」ことで守っている。
  # socket-proxy 側が広がると、コード側の判定を誤ったときに何も止まらない。
  monitor_perms=$(awk '
    /^  socket-proxy:/ { inblock = 1; next }
    /^  [a-zA-Z0-9_-]+:/ { inblock = 0 }
    inblock && /^[[:space:]]*(IMAGES|VOLUMES|NETWORKS|BUILD|EXEC):[[:space:]]*1/ { print }
  ' "${lab_compose}")

  if [ -n "${monitor_perms}" ]; then
    fail "監視用の socket-proxy に展開の権限が付いています(二層の境界が壊れます):"
    echo "${monitor_perms}" >&2
  else
    ok "監視用の socket-proxy は展開の権限を持っていません。"
  fi

  # 展開用でも任意コマンド実行とイメージ作成は要らない
  deploy_perms=$(awk '
    /^  deploy-proxy:/ { inblock = 1; next }
    /^  [a-zA-Z0-9_-]+:/ { inblock = 0 }
    inblock && /^[[:space:]]*(EXEC|BUILD|COMMIT):[[:space:]]*1/ { print }
  ' "${lab_compose}")

  if [ -n "${deploy_perms}" ]; then
    fail "展開用の deploy-proxy に exec / build の権限が付いています:"
    echo "${deploy_perms}" >&2
  else
    ok "展開用の deploy-proxy にも exec / build の権限はありません。"
  fi
fi

# --- 3. .env をGit管理していないこと ---

tracked_env=$(cd "${ROOT_DIR}" && git ls-files | grep -E '(^|/)\.env$' || true)
if [ -n "${tracked_env}" ]; then
  fail "実際の値が入りうる .env がGit管理されています: ${tracked_env}"
else
  ok ".env はGit管理されていません。"
fi

# --- 4. EF Coreの版が9.0で固定されていること ---

ef_versions=$(grep -rhoE 'Microsoft\.EntityFrameworkCore[^"]*" Version="[0-9.]+"' \
  "${ROOT_DIR}/server" --include='*.csproj' | grep -oE 'Version="[0-9.]+"' | sort -u || true)

if echo "${ef_versions}" | grep -qE 'Version="(1[0-9]|[1-9][0-9])\.'; then
  fail "EF Coreが10.x以降になっています。9.0で固定してください。"
  echo "${ef_versions}" >&2
else
  ok "EF Coreは9.x のままです。"
fi

# --- 5. 依存に既知の脆弱性が無いこと ---
#
# 推移的な依存で入り込むものも見る。
# 実際には通らないコードパスであっても、依存として残さない方針。

if command -v dotnet >/dev/null 2>&1; then
  vulnerable=$(cd "${ROOT_DIR}/server" \
    && dotnet list package --include-transitive --vulnerable 2>/dev/null \
    | grep -E '^\s+> ' || true)

  if [ -n "${vulnerable}" ]; then
    fail "依存に既知の脆弱性があります。"
    echo "${vulnerable}" >&2
  else
    ok "依存に既知の脆弱性はありません。"
  fi
else
  echo "SKIP: dotnet が無いため依存の脆弱性を確認できません。" >&2
fi

# --- 6. 運用上の歯止めがComposeに入っていること ---
#
# healthcheck が無いと、ハングしたコンテナが放置される。
# ログ上限が無いと、監視システム自身がディスクを埋める。
# メモリ上限が無いと、暴走時にホストごと巻き込む。

compose="${ROOT_DIR}/deploy/docker-compose.yml"
services="nginx web api worker mysql"

for key in healthcheck logging mem_limit; do
  missing=""
  for svc in ${services}; do
    # 対象サービスのブロックだけを切り出して確認する
    block=$(awk -v svc="  ${svc}:" '
      $0 == svc { inside = 1; next }
      /^  [a-z]/ { inside = 0 }
      inside { print }
    ' "${compose}")

    if ! echo "${block}" | grep -q "${key}"; then
      missing="${missing} ${svc}"
    fi
  done

  if [ -n "${missing}" ]; then
    fail "${key} が設定されていないサービスがあります:${missing}"
  else
    ok "すべてのサービスに ${key} が設定されています。"
  fi
done

# --- 7. セキュリティヘッダがnginxに入っていること ---

nginx_conf="${ROOT_DIR}/deploy/nginx/default.conf"
for header in X-Content-Type-Options X-Frame-Options Content-Security-Policy Referrer-Policy; do
  if ! grep -q "${header}" "${nginx_conf}"; then
    fail "nginxに ${header} が設定されていません。"
  fi
done

if grep -q "limit_req_zone" "${nginx_conf}"; then
  ok "nginxにセキュリティヘッダとログイン試行の制限が入っています。"
else
  fail "nginxにログイン試行の制限(limit_req)が設定されていません。"
fi

echo
if [ "${failures}" -gt 0 ]; then
  echo "${failures} 件の問題があります。リリース前に解消してください。" >&2
  exit 1
fi

echo "すべての確認項目を満たしています。"
