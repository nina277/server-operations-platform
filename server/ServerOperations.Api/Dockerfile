# ビルドコンテキストは server/ ディレクトリを想定する
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ServerOperations.Api/ServerOperations.Api.csproj ServerOperations.Api/
COPY ServerOperations.Core/ServerOperations.Core.csproj ServerOperations.Core/
RUN dotnet restore ServerOperations.Api/ServerOperations.Api.csproj
COPY ServerOperations.Api/ ServerOperations.Api/
COPY ServerOperations.Core/ ServerOperations.Core/
RUN dotnet publish ServerOperations.Api/ServerOperations.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# healthcheck で /api/health/live を叩くために入れる。
# ランタイム画像にはHTTPクライアントが含まれていない。
# アプリの依存ではないため dotnet list package の対象にはならない。
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Data Protection の鍵置き場。名前付きボリュームをここへマウントするため、
# **イメージ側にディレクトリを作って所有者を合わせておく**必要がある。
# 用意しないとボリュームがroot所有で作られ、非rootで動くアプリが鍵を書けない。
# 鍵が書けないとMFAの設定も秘密値の暗号化もできず、管理操作が一切行えなくなる。
RUN mkdir -p /app/keys && chown -R $APP_UID:$APP_UID /app/keys

USER $APP_UID
ENTRYPOINT ["dotnet", "ServerOperations.Api.dll"]
