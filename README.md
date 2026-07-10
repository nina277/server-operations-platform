# server-operations-platform

自律型サーバー運用支援システム。Dockerを中心とする自宅サーバーを監視し、ルール・履歴・必要時だけの外部AIで診断し、安全な範囲で復旧を支援するセルフホスト型運用プラットフォーム。

既存のHome Cloud Platformとは完全に独立した新システムであり、リポジトリ・データベース・認証・Docker Composeプロジェクトを共有しない。

## ディレクトリ構成

    server/       ASP.NET Core Web API (.NET 10) と Worker
    client-web/   Vue 3 + TypeScript + Vite のフロントエンド
    deploy/       Docker Compose・nginx設定・環境変数テンプレート
    docs/         ドキュメント
    scripts/      運用・開発補助スクリプト

## 技術構成

| 領域 | 採用技術 |
|---|---|
| Backend | C# / ASP.NET Core Web API (.NET 10) |
| Frontend | Vue 3 / TypeScript / Vite / Pinia / Vue Router / axios |
| Database | MySQL 8.4 / utf8mb4 / InnoDB |
| Reverse proxy | Nginx 1.27-alpine |
| 配置 | Docker Compose |

## 起動手順

前提: Docker / Docker Compose が利用できること。

    cd deploy
    cp .env.example .env
    # .env を編集し、MySQLパスワード等のダミー値を実際の値へ変更する
    docker compose up -d --build

起動確認:

    curl http://localhost:8080/api/health/live   # APIのliveness
    curl http://localhost:8080/api/health/ready  # APIのreadiness
    curl http://localhost:8080/                  # フロントエンド

## 環境変数

`deploy/.env.example` を参照。`.env` はGit管理しない。

| 変数 | 説明 | 既定値 |
|---|---|---|
| HTTP_PORT | nginxが外部公開するHTTPポート | 8080 |
| ASPNETCORE_ENVIRONMENT | ASP.NET Core実行環境 | Production |
| TZ | タイムゾーン | Asia/Tokyo |
| MYSQL_ROOT_PASSWORD | MySQL rootパスワード | (ダミー。要変更) |
| MYSQL_DATABASE | データベース名 | server_operations |
| MYSQL_USER | アプリ用DBユーザー | serverops |
| MYSQL_PASSWORD | アプリ用DBパスワード | (ダミー。要変更) |
| JWT_SIGNING_KEY | JWT署名鍵(32文字以上) | (ダミー。要変更) |
| INITIAL_ADMIN_USERNAME | 初期管理者ユーザー名(初回起動時のみ使用) | admin |
| INITIAL_ADMIN_PASSWORD | 初期管理者パスワード(12文字以上) | (ダミー。要変更) |

## ポート方針

- 外部へ公開するポートは **nginxの `HTTP_PORT`(既定8080)のみ**。
- MySQL・API・Workerは外部ポートを公開しない。nginx経由でのみAPIへ到達できる。
- MySQL・Workerは内部専用ネットワーク(`backend`)に置き、外部との直接通信を遮断する。

## ローカル開発

Backend:

    cd server
    dotnet run --project ServerOperations.Api   # http://localhost:5275

Frontend:

    cd client-web
    npm install
    npm run dev        # http://localhost:5173 (/api は 5275 へプロキシ)

テスト・Lint:

    cd server && dotnet build && dotnet test
    cd client-web && npm run test:unit -- --run && npm run lint && npm run type-check

## CI

`main` へのPull Requestとpushで GitHub Actions(`.github/workflows/ci.yml`)が自動実行される。

- Backend: `dotnet restore` → `dotnet build` → `dotnet test`
- Frontend: `npm ci` → `lint` → `type-check` → `test:unit` → `build`

CIが失敗しているPRはマージしない。

## 開発フロー

ブランチ戦略は GitHub Flow を採用する。詳細は [CONTRIBUTING.md](./CONTRIBUTING.md) を参照。
