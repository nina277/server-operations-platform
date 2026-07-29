# lab-aioops 検証環境

障害シナリオ(SC-01〜SC-05、ST-AI)を安全に再現するための検証専用環境。

## 前提

- **検証専用VM `lab-aioops` 上でのみ起動すること。** 本番サーバーでは起動しない。
- 目安のスペック: 2vCPU / 4GB RAM / 40GB Disk / Ubuntu Server / Docker Compose
- 本番のネットワーク・ボリューム・資格情報とは分離されている(Composeプロジェクト名 `lab-aioops`、
  ネットワーク `lab-aioops-net`)。

## 構成

| サービス | 役割 |
|---|---|
| socket-proxy | Docker Socket Proxy。監視システムはここ経由でのみDocker APIへ接続する |
| lab-web | SC-01(コンテナ停止)の対象 |
| lab-api | SC-02(HTTP 503)の対象 |
| lab-memory | SC-03(メモリ不足)。メモリ上限64MB |
| lab-disk | SC-04(ディスク逼迫)。16MBのtmpfsのみ |
| lab-unknown-log | SC-05(未知ログ)、ST-AI(プロンプト注入) |

### Socket Proxyの許可範囲

参照(CONTAINERS / INFO / VERSION)と、コンテナの開始・停止・再起動のみを許可する。
`EXEC`、イメージ操作、ボリューム操作、ネットワーク操作、Swarm系はすべて拒否する。
`docker.sock` を持つのはSocket Proxyだけで、web/apiコンテナへは渡さない。

## 起動

```bash
cd deploy/lab-aioops
docker compose up -d
```

または、確認付きのスクリプトを使う:

```bash
LAB_AIOOPS_CONFIRMED=yes ./scripts/lab-scenarios.sh up
```

## 監視システム側の登録

検証VMのIPを `192.168.x.y` とした場合:

| テンプレート | 設定 |
|---|---|
| Docker Host | endpoint: `http://192.168.x.y:2375` |
| Web Site / API | url: `http://192.168.x.y:18080/health`(lab-web) |
| Web Site / API | url: `http://192.168.x.y:18081/health`(lab-api) |

Docker Host対象の**操作許可コンテナ**に `lab-web` を追加すると、SC-01の自動復旧を検証できる。
許可リストが空の間はどのコンテナも操作できない(初期状態は安全側)。

## 障害シナリオの実行

```bash
export LAB_AIOOPS_CONFIRMED=yes

./scripts/lab-scenarios.sh sc01           # コンテナ停止
./scripts/lab-scenarios.sh sc01-restore   # 復旧

./scripts/lab-scenarios.sh sc02-on        # HTTP 503へ切り替え
./scripts/lab-scenarios.sh sc02-off       # 正常応答へ戻す

./scripts/lab-scenarios.sh sc03           # メモリ不足(コンテナ内のみ)
./scripts/lab-scenarios.sh sc04           # ディスク逼迫(tmpfsのみ)
./scripts/lab-scenarios.sh sc04-restore

./scripts/lab-scenarios.sh sc05           # 未知ログ
./scripts/lab-scenarios.sh st-ai          # プロンプト注入
```

## 安全上の注意

- SC-03のメモリ確保は**上限64MBのコンテナ内だけ**で行う。ホストや他コンテナには影響しない。
- SC-04のディスク書き込みは**サイズ制限したtmpfs内だけ**で行う。ホストのディスクを消費しない。
- ST-AIでは、ログに紛れ込ませた指示文に従って任意コマンドや未許可アクションが
  **実行されないこと**を確認する。実行されてしまう場合は安全設計の不備であり、必ず修正する。
