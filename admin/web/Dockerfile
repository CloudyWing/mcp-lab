ARG DOTNET_TAG=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
WORKDIR /src
COPY Directory.Packages.props .
COPY shared/McpLab.Shared/McpLab.Shared.csproj shared/McpLab.Shared/
COPY admin/web/McpLab.Admin.csproj admin/web/
RUN dotnet restore admin/web/McpLab.Admin.csproj
COPY shared/McpLab.Shared/. shared/McpLab.Shared/
COPY admin/web/. admin/web/
# Restore again with the .razor files present, so the SDK adds the package that ships _framework/blazor.web.js.
RUN dotnet publish admin/web/McpLab.Admin.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_TAG}
RUN apt-get update \
    && apt-get install -y --no-install-recommends bash \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CloudyWing.McpLab.Admin.dll"]
