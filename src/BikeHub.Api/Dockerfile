FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/auth/keys && chown -R $APP_UID:$APP_UID /app/data/auth
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY .editorconfig Directory.Build.props ./

COPY src/ ./src/

WORKDIR "/src/src/BikeHub.Api"

RUN dotnet restore --disable-parallel

FROM sdk AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BikeHub.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore --disable-build-servers -m:1 /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BikeHub.Api.dll"]
