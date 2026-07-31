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
USER $APP_UID
ENTRYPOINT ["dotnet", "ServerOperations.Api.dll"]
