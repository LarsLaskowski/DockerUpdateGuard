ARG APP_VERSION=1.0.0

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP_VERSION
WORKDIR /src

COPY Directory.Packages.props ./
COPY Directory.Build.props ./
COPY src/DockerUpdateGuard/DockerUpdateGuard.csproj src/DockerUpdateGuard/
COPY src/DockerUpdateGuard.Data/DockerUpdateGuard.Data.csproj src/DockerUpdateGuard.Data/
COPY src/DockerUpdateGuard.Telemetry/DockerUpdateGuard.Telemetry.csproj src/DockerUpdateGuard.Telemetry/
RUN dotnet restore src/DockerUpdateGuard/DockerUpdateGuard.csproj

COPY . .
RUN dotnet publish src/DockerUpdateGuard/DockerUpdateGuard.csproj -c Release -o /app/publish -p:Version=$APP_VERSION -p:InformationalVersion=$APP_VERSION

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_VERSION
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DockerUpdateGuard__DisplayVersion=$APP_VERSION

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DockerUpdateGuard.dll"]
