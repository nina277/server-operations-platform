# リリース手順

自宅サーバーへ配置・更新するときの手順。**手を止めて確認する箇所を明示する。**

## 前提

- Docker / Docker Compose が使えること
- 外部へ公開するのは nginx の `HTTP_PORT`(既定8080)だけであること
- 既存の別システムとは独立したComposeプロジェクト(`server-operations`)であること

---

## 1. リリース前の確認

### 自動試験がすべて通っていること

```bash
cd server && dotnet test
cd client-web && npm run test:unit -- --run && npm run type-check && npm run lint && npm run build
```

CIが緑であることをもって代えてもよい。

### イメージのタグが固定されていること

`latest` だけを使う運用はしない。`deploy/docker-compose.yml` で使う外部イメージは
すべて版を指定していることを確認する。

```bash
grep -n 'image:' deploy/docker-compose.yml deploy/lab-aioops/docker-compose.yml
```

期待する状態の例:

- `nginx:1.27-alpine`
- `mysql:8.4`
- `node:22-alpine`(ビルド用)

`:latest` や版の無い指定が現れたら、リリースを止めて版を固定する。

### 秘密情報がコミットされていないこと

```bash
git status --short           # .env が現れないこと
grep -rn 'BEGIN PRIVATE KEY\|password=' --include='*.json' --include='*.yml' . | grep -v node_modules
```

`.env` はGit管理しない。実際のIPアドレス・トークン・パスワードをコミットしない。

### 依存の版と脆弱性

- **EF Core は 9.0 で固定する。10.x へ更新しない。**
- 依存を上げたときは、移行(マイグレーション)の生成物に差分が出ていないか確認する

推移的な依存も含めて脆弱性を確認する。

```bash
cd server && dotnet list package --include-transitive --vulnerable
```

`./scripts/check-release-guards.sh` でも同じ確認を行う(CIでも実行される)。
実際には通らないコードパスであっても、依存として残さない方針とする。

---

## 2. 初回の配置

```bash
cd deploy
cp .env.example .env
```

`.env` を編集し、**ダミー値をすべて実際の値へ変える**。

| 変数 | 注意 |
|---|---|
| `MYSQL_ROOT_PASSWORD` / `MYSQL_PASSWORD` | 使い回さない |
| `JWT_SIGNING_KEY` | 32文字以上。`openssl rand -base64 48` で作る |
| `INITIAL_ADMIN_PASSWORD` | 12文字以上。**初回起動後に `.env` から消す** |
| `VITE_FIREBASE_*` | Push通知を使う場合のみ。未設定なら通知は「未設定」として扱われる |

起動:

```bash
docker compose up -d --build
```

確認:

```bash
curl http://localhost:8080/api/health/live    # liveness
curl http://localhost:8080/api/health/ready   # readiness(DB接続まで確認)
curl -o /dev/null -w '%{http_code}\n' http://localhost:8080/api/v1/me   # 401 が返ること
```

最後の1つは**未認証で401が返ること**を確かめるためのもの。200が返るなら認証が効いていない。

### 初回ログイン後にすること

順番が重要。**MFAを設定するまで管理操作は一切できない。**
設定変更・復旧の実行・監査ログの参照は、MFAが有効で直近に認証していることを要求する。

1. 初期管理者でログインする
2. ヘッダーの利用者名から**アカウント画面**を開く
3. **MFAを設定する** — QRコードを認証アプリで読み取り、コードを入力して有効にする
4. 同じ画面で**パスワードを変更する**(12文字以上)。
   変更すると他の端末のログインは解除される
5. `.env` から `INITIAL_ADMIN_PASSWORD` を消す
6. 接続を許可するネットワーク範囲(CIDR)を登録する
7. 監視対象を登録し、接続試験が通ることを確かめる
8. **通知**と**バックアップの保存先**を設定する(後述)
9. **自動復旧は既定でOFF。**有効にする場合は、対象ごとに許可コンテナを先に設定する

管理操作で403が返る場合、まずアカウント画面でMFAの状態を確認する。
MFAの認証が古くなっている場合も403になるため、再度ログインし直す。

### 通知の設定

設定 → 通知。**通知は既定で無効。**設定するまで何も送られない。

1. **秘密情報**でSMTPパスワードを登録する(`smtp-password`)。
   Push通知を使う場合は `fcm-service-account` も登録する
2. 通知の画面で、通知する重大度・再通知の間隔を決める
3. メール通知を使う場合は、SMTPサーバー・ポート・差出人アドレス・宛先を入れて保存する

SMTPサーバーは**ホスト名またはIPアドレスだけ**を指定する。URLは受け付けない。
localhost・リンクローカル・メタデータIP宛は登録できない。

メール通知を有効にしたままSMTPサーバーや宛先が空だと保存は拒否される。
「有効なのに送れない」状態を作らないため。設定途中の状態は無効のまま保存できる。

保存したら「テスト通知を送る」で実際に届くことを確かめる。
**本物の障害を待たずに設定を確認できる唯一の手段。**宛先は保存済みのものだけを使い、
画面から任意の宛先を指定することはできない(踏み台にされるため)。
送信したことは監査に残る。

### メンテナンス期間(計画停止の前に)

メンテナンス → 期間を登録する。

サーバーを自分で更新・再起動する前に登録しておくと、その作業を障害として
通知したり、自動復旧が動いて作業を邪魔したりしなくなる。

- **検知そのものは止まらない。**期間中に起きたことはインシデントとして残る
- 通知と自動復旧のどちらを止めるかは個別に選べる。両方止めない期間は登録できない
- 対象を選ばなければすべての監視対象に効く
- 登録と取り消しはどちらも監査に残る

期間中に自動復旧を止めた場合、監査ログの `recovery.auto.denied` に理由が残る。
「なぜ自動復旧が動かなかったのか」はここで確認できる。

### バックアップの保存先

設定 → バックアップ → バックアップの保存先。**定期バックアップは既定で無効。**

1. **秘密情報**で `backup-secret-key` を登録する
2. 保存先のURL(S3互換。MinIOなど)・バケット名・保持する世代数を入れて保存する
3. 「接続を試す」で到達できることを確かめる
4. 「定期バックアップを行う」を有効にして保存する

保存先も接続先の検証を通る必要がある。localhostやリンクローカルのアドレスは登録できない。
有効にしたまま保存先やバケット名が空だと保存は拒否される。

---

## 3. 更新

```bash
cd deploy
git pull
docker compose up -d --build
```

DBの移行はAPI起動時に自動で適用される(`Database__AutoMigrate=true`)。

### 更新前に取るもの

**移行を伴う更新の前は必ずバックアップを取る。**

```bash
# 画面から: 設定 → バックアップ → いま実行する
# または直接:
docker compose exec mysql mysqldump -u root -p"$MYSQL_ROOT_PASSWORD" \
  --single-transaction --routines --default-character-set=utf8mb4 \
  "$MYSQL_DATABASE" > backup-$(date +%Y%m%d-%H%M%S).sql
```

### 更新後の確認

```bash
docker compose ps                                  # すべて healthy / running
curl http://localhost:8080/api/health/ready        # readiness
docker compose logs --tail=50 api worker           # 例外が出ていないこと
```

画面で次を確認する。

1. ログインできること
2. ダッシュボードに監視対象が出ること
3. 監査ログに更新前後の記録が残っていること

---

## 4. 切り戻し

更新後に問題が起きた場合。

```bash
cd deploy
git checkout <前のタグまたはコミット>
docker compose up -d --build
```

**DBの移行は自動では戻らない。** 移行を含む更新を切り戻すときは、
更新前に取ったバックアップから復元する。

```bash
docker compose exec -T mysql mysql -u root -p"$MYSQL_ROOT_PASSWORD" \
  "$MYSQL_DATABASE" < backup-YYYYmmdd-HHMMSS.sql
```

復元は既存データを置き換える。**実行前に、いま入っているデータを失ってよいか必ず確認する。**

---

## 5. 配置後に効いていることを確かめる安全条件

リリースのたびに、次が保たれていることを確認する。

| 確認すること | 見かた |
|---|---|
| 外部に開いているポートが1つだけ | `docker compose ps` の PORTS 欄 |
| MySQL・API・Workerが外部へ出ていない | 同上。ポート公開が無いこと |
| Dockerソケットがweb/apiに渡っていない | `docker compose config` で `docker.sock` の行を確認 |
| 未認証でAPIが叩けない | `curl` で保護された口が401を返すこと |
| 秘密値が画面・API応答に出ない | 設定画面の秘密情報が「設定済み」とだけ出ること |
| 自動復旧の既定がOFF | 新規に登録した対象で自動復旧が無効であること |
| 監査ログが残っている | 監査ログ画面に配置作業前後の記録があること |

Dockerソケットの確認は特に重要で、**web/apiコンテナへ直接渡してはならない**。
Dockerへの接続はSocket Proxy経由に限る。

---

## 6. 定期的に見るもの

| 頻度 | 内容 |
|---|---|
| 毎日 | 通知・未解決インシデント |
| 毎週 | 運用実績(検知から通知までの時間・自動実行を止めた理由) |
| 毎週 | バックアップが成功しているか(設定 → バックアップ → 実行履歴) |
| 毎月 | 監査ログの見直し。AI利用量が上限に対してどうか |
| 随時 | 依存の脆弱性情報。ただしEF Coreは9.0で固定する |

バックアップは**取れているだけでは足りない**。復元できることを定期的に確かめる。
