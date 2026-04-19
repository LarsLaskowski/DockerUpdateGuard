FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/DockerUpdateGuard/DockerUpdateGuard.csproj src/DockerUpdateGuard/
COPY src/DockerUpdateGuard.Data/DockerUpdateGuard.Data.csproj src/DockerUpdateGuard.Data/
RUN dotnet restore src/DockerUpdateGuard/DockerUpdateGuard.csproj

COPY . .
RUN dotnet publish src/DockerUpdateGuard/DockerUpdateGuard.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DockerUpdateGuard.dll"]