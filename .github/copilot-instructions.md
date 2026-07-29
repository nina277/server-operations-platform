# GitHub Copilot 向けリポジトリ指示

## 応答言語

**すべての回答・レビューコメント・要約・提案を日本語で書くこと。**
コードレビューの指摘、Pull Requestの説明、チャットの回答を含め、例外なく日本語で出力する。

コード・識別子・エラーメッセージ・ログ出力・コマンドはそのまま(英語のまま)引用してよい。
説明文・理由・提案は日本語で書く。

## プロジェクト概要

卒業研究「自律型サーバー運用支援システム」。Dockerを中心とする自宅サーバーを監視し、
ルール・履歴・必要時だけの外部AIで診断し、安全な範囲で復旧を支援するセルフホスト型の運用プラットフォーム。

## 技術構成

| 領域 | 採用技術 |
|---|---|
| Backend | C# / ASP.NET Core Web API (.NET 10) |
| ORM | EF Core 9.0固定、Pomelo.EntityFrameworkCore.MySql 9.0.0 |
| Database | MySQL 8.4 / utf8mb4 / InnoDB |
| Frontend | Vue 3 / TypeScript / Vite / Pinia / Vue Router / axios |
| Job | Hangfire 1.8 |
| 配置 | Docker Compose |

## プロジェクト構成

- `server/ServerOperations.Core` … Entity・DbContext・Repository・Adapter・ドメインサービス(API/Worker共有)
- `server/ServerOperations.Api` … Web API(受付・検証・応答)
- `server/ServerOperations.Worker` … Hangfireによる収集・復旧の実行
- `client-web` … Vue 3のフロントエンド
- `deploy` … Docker Compose・nginx設定

## 設計上の約束

- Controller → Service → Repository の3層を守る。Controllerは受付と応答だけを担う。
- APIレスポンスは `ApiResponse<T>` で統一する。
- EF Coreは9.0固定。10.xへ更新しない。
- 日時列は `datetime(6)`、UTCで保存する。
- 監査ログには操作者・IPアドレス・User-Agent・対象・操作・結果・時刻を必ず保存する。
- パスワードはBCrypt。リフレッシュトークンはSHA-256ハッシュのみ保存し、ローテーションする。
- 秘密値はData Protectionで暗号化して保存し、API応答・監査詳細・アプリログへ出さない。

## 安全設計(特に重視するレビュー観点)

- Dockerソケットをweb/apiコンテナへ直接マウントしない。接続先はSocket Proxyまたは
  TLS保護済みAPI(http/https)のみ許可し、`unix://` 等を拒否する。
- 接続先URLはSSRF対策として、localhost・リンクローカル・メタデータIP・マルチキャストを拒否する。
  リダイレクトは追跡しない。
- 接続試験・収集は登録済みの監視対象IDから実行する。任意URLを受け取るAPIを作らない。
- 復旧は許可リスト・対象別の許可操作・承認・冪等性・クールダウン・回数上限・
  サーキットブレーカー・ヘルスチェックをすべて通過させる。
- High危険度の操作(DB再起動・VM操作・削除・任意コマンド)を実行するAPIを作らない。
- 復旧の実行はWorkerだけが行う。APIはキューへ積むのみ。
- ログ・通知本文は保存・送信前に秘密情報をマスクする。通知にログ全文を含めない。
- 生成AIが返したコマンド・SQL・URL・スクリプトをそのまま実行しない。
  推奨アクションIDは対象能力・許可リスト・危険度・承認条件をService層で再検証する。

## テスト

- Backendは xUnit(`server/ServerOperations.Api.Tests`)。
- Frontendは Vitest。
- Pull Requestでは GitHub Actions が build・test・lint・型チェックを実行する。
