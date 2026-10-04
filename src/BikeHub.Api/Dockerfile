FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Исправлено: заменен aspnet на sdk для этапа сборки
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1. Копируем всю папку с исходниками
COPY src/ ./src/

# 2. Переходим в папку главного проекта
WORKDIR "/src/src/BikeHub.Api"

# 3. Восстанавливаем зависимости и собираем проект
RUN dotnet restore
RUN dotnet build "BikeHub.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Исправлено: ссылаемся на sdk вместо несуществующего build
FROM sdk AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BikeHub.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Исправлено: ссылаемся на base вместо несуществующего base
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BikeHub.Api.dll"]
