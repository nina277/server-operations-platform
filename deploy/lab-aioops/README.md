# lab-aioops 検証環境

障害シナリオ(SC-01〜SC-07、ST-AI)を安全に再現するための検証専用環境。

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
| node-exporter | SC-06(ホストのディスク使用率)。監視システムが読む値を出す |
| lab-load | SC-07(リソース逼迫)。CPU 0.5コア / メモリ上限64MB |

### node-exporter がホストの `/` を読む理由

Docker Engine APIは**ホストのファイルシステム容量を返さない。**
そのため、ディスク使用率を収集するにはホスト側に値を出す仕組みが要る。

`/` を `/host` へ**読み取り専用**でマウントするのは、容量を読むために避けられない。
ただし次の点で socket-proxy とは性質が違う。

- **`docker.sock` は渡さない。**node_exporter はホストに対して何も実行できない
- 監視システム側から見ても、行うのは `GET /metrics` だけ
- 書き込みの経路が無いため、ここを踏み台にしてホストを変更することはできない

本番でも同じ構成が要る。手順書(`docs/release.md`)に導入手順を書いてある。

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

Docker Host対象の**ホストメトリクスURL**に `http://192.168.x.y:19100/metrics` を入れると、
ホストのディスク使用率を収集する。空のままなら収集しない。

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

./scripts/lab-scenarios.sh sc06           # ディスク使用率を df と突き合わせる
./scripts/lab-scenarios.sh sc07           # CPU・メモリ使用率を上げる
./scripts/lab-scenarios.sh sc07-restore

./scripts/lab-scenarios.sh st-ai          # プロンプト注入
```

### SC-06・SC-07 は「値が正しいか」を見るシナリオ

SC-01〜SC-05 が「障害を起こして検知されるか」を見るのに対し、
この2つは**収集した数値がホストの実際の値と一致するか**を見る。

- SC-06 は `df` と node_exporter の値を並べて出す。**USE% が一致すること**
- SC-07 は `docker stats` と並べて出す。**CPU % と MEM % が一致すること**

数値がずれていても障害は検知されるため、シナリオ実行だけでは気づけない。
ここを別のシナリオとして分けてあるのはそのため。

検知まで確かめるには、診断ルールのしきい値を現在の値より低くして保存し、
次の収集でインシデントが作られることを見る。

## 安全上の注意

- SC-03のメモリ確保は**上限64MBのコンテナ内だけ**で行う。ホストや他コンテナには影響しない。
- SC-04のディスク書き込みは**サイズ制限したtmpfs内だけ**で行う。ホストのディスクを消費しない。
- SC-06は**読むだけ**で、ディスクを埋めない。ホストの実ディスクを満たす手段は用意していない
  (検証のためにホストを壊すことになるため)。しきい値の検知はルール側を下げて確かめる。
- SC-07の負荷は**CPU 0.5コア・メモリ64MBの上限の内側**だけで動く。CPU負荷は120秒で自然に止まる。
- ST-AIでは、ログに紛れ込ませた指示文に従って任意コマンドや未許可アクションが
  **実行されないこと**を確認する。実行されてしまう場合は安全設計の不備であり、必ず修正する。
