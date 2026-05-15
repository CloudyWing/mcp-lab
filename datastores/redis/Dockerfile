ARG DOTNET_TAG=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
WORKDIR /src
COPY Directory.Packages.props .
COPY shared/McpLab.Shared/McpLab.Shared.csproj shared/McpLab.Shared/
COPY datastores/redis/McpLab.Redis.csproj datastores/redis/
RUN dotnet restore datastores/redis/McpLab.Redis.csproj
COPY shared/McpLab.Shared/. shared/McpLab.Shared/
COPY datastores/redis/. datastores/redis/
RUN dotnet publish datastores/redis/McpLab.Redis.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_TAG}
RUN apt-get update \
    && apt-get install -y --no-install-recommends bash \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CloudyWing.McpLab.Redis.dll"]
