FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .editorconfig Directory.Build.props ./
COPY src/BikeHub.Mail.Contracts/BikeHub.Mail.Contracts.csproj src/BikeHub.Mail.Contracts/
COPY src/BikeHub.Mail/BikeHub.Mail.csproj src/BikeHub.Mail/
RUN dotnet restore src/BikeHub.Mail/BikeHub.Mail.csproj --disable-parallel --verbosity normal
COPY src/BikeHub.Mail.Contracts/ src/BikeHub.Mail.Contracts/
COPY src/BikeHub.Mail/ src/BikeHub.Mail/
RUN dotnet publish src/BikeHub.Mail/BikeHub.Mail.csproj -c Release -o /app/publish --no-restore --disable-build-servers -m:1 /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BikeHub.Mail.dll"]
