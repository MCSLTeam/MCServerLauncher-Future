FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

EXPOSE 11452

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj", "MCServerLauncher.Daemon/"]
COPY ["MCServerLauncher.Common/MCServerLauncher.Common.csproj", "MCServerLauncher.Common/"]
RUN dotnet restore "MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj"
COPY . .
WORKDIR "/src/MCServerLauncher.Daemon"
RUN dotnet build "./MCServerLauncher.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MCServerLauncher.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MCServerLauncher.Daemon.dll"]
