FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .editorconfig Directory.Build.props ./
COPY src/BikeHub.Mail.Contracts/BikeHub.Mail.Contracts.csproj src/BikeHub.Mail.Contracts/
COPY src/BikeHub.Mail/BikeHub.Mail.csproj src/BikeHub.Mail/
COPY tools/BikeHub.Tools/BikeHub.Tools.csproj tools/BikeHub.Tools/
RUN dotnet restore tools/BikeHub.Tools/BikeHub.Tools.csproj --disable-parallel
COPY src/BikeHub.Mail.Contracts/ src/BikeHub.Mail.Contracts/
COPY src/BikeHub.Mail/ src/BikeHub.Mail/
COPY tools/BikeHub.Tools/ tools/BikeHub.Tools/
RUN dotnet publish tools/BikeHub.Tools/BikeHub.Tools.csproj -c Release -o /app/publish --no-restore --disable-build-servers -m:1 /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "BikeHub.Tools.dll"]
