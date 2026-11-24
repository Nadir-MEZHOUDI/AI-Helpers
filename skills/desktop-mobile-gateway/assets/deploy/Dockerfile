# Build context: the repository root (see deploy/gateway/docker-compose.yml and azure-pipelines-gateway.yml).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ARG APP_UID=1000
USER ${APP_UID}

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["MyApp.Contract/MyApp.Contract.csproj", "MyApp.Contract/"]
COPY ["MyApp.Gateway.Data/MyApp.Gateway.Data.csproj", "MyApp.Gateway.Data/"]
COPY ["MyApp.Mobile/MyApp.Mobile.csproj", "MyApp.Mobile/"]
COPY ["MyApp.Gateway/MyApp.Gateway.csproj", "MyApp.Gateway/"]
RUN dotnet restore "MyApp.Gateway/MyApp.Gateway.csproj"

COPY MyApp.Contract/ MyApp.Contract/
COPY MyApp.Gateway.Data/ MyApp.Gateway.Data/
COPY MyApp.Mobile/ MyApp.Mobile/
COPY MyApp.Gateway/ MyApp.Gateway/
RUN dotnet publish "MyApp.Gateway/MyApp.Gateway.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyApp.Gateway.dll"]
