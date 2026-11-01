FROM node:22-alpine AS frontend
WORKDIR /web
COPY Web/package.json Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/auth/keys && chown -R $APP_UID:$APP_UID /app/data/auth
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Исправлено: заменен aspnet на sdk для этапа сборки
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY .editorconfig Directory.Build.props ./

# 1. Копируем всю папку с исходниками
COPY src/ ./src/

# 2. Переходим в папку главного проекта
WORKDIR "/src/src/BikeHub.Api"

# 3. Восстанавливаем зависимости и собираем проект
RUN dotnet restore --disable-parallel

# Исправлено: ссылаемся на sdk вместо несуществующего build
FROM sdk AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BikeHub.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore --disable-build-servers -m:1 /p:UseAppHost=false

# Исправлено: ссылаемся на base вместо несуществующего base
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=frontend /web/dist ./wwwroot
ENTRYPOINT ["dotnet", "BikeHub.Api.dll"]
