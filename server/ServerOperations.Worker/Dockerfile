# ビルドコンテキストは server/ ディレクトリを想定する
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ServerOperations.Worker/ServerOperations.Worker.csproj ServerOperations.Worker/
COPY ServerOperations.Core/ServerOperations.Core.csproj ServerOperations.Core/
RUN dotnet restore ServerOperations.Worker/ServerOperations.Worker.csproj
COPY ServerOperations.Worker/ ServerOperations.Worker/
COPY ServerOperations.Core/ ServerOperations.Core/
RUN dotnet publish ServerOperations.Worker/ServerOperations.Worker.csproj -c Release -o /app /p:UseAppHost=false

# Data Protection(監視対象の資格情報を復号する)が
# Microsoft.AspNetCore.App のフレームワーク参照を持つため、
# runtime 画像では起動できない。**aspnet 画像が要る。**
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Data Protection の鍵置き場。名前付きボリュームをここへマウントするため、
# **イメージ側にディレクトリを作って所有者を合わせておく**必要がある。
# 用意しないとボリュームがroot所有で作られ、非rootで動くアプリが鍵を書けない。
# 鍵が書けないとMFAの設定も秘密値の暗号化もできず、管理操作が一切行えなくなる。
RUN mkdir -p /app/keys && chown -R $APP_UID:$APP_UID /app/keys

USER $APP_UID
ENTRYPOINT ["dotnet", "ServerOperations.Worker.dll"]
