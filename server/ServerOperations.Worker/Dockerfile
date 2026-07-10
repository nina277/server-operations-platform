# ビルドコンテキストは server/ ディレクトリを想定する
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ServerOperations.Worker/ServerOperations.Worker.csproj ServerOperations.Worker/
RUN dotnet restore ServerOperations.Worker/ServerOperations.Worker.csproj
COPY ServerOperations.Worker/ ServerOperations.Worker/
RUN dotnet publish ServerOperations.Worker/ServerOperations.Worker.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "ServerOperations.Worker.dll"]
