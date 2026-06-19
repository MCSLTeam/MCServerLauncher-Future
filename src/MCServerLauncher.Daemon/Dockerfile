FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

EXPOSE 11452

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj", "src/MCServerLauncher.Daemon/"]
COPY ["src/MCServerLauncher.Common/MCServerLauncher.Common.csproj", "src/MCServerLauncher.Common/"]
COPY ["generators/MCServerLauncher.Daemon.Generators/MCServerLauncher.Daemon.Generators.csproj", "generators/MCServerLauncher.Daemon.Generators/"]
RUN dotnet restore "src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj"
COPY . .
WORKDIR "/src/src/MCServerLauncher.Daemon"
RUN dotnet build "./MCServerLauncher.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MCServerLauncher.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MCServerLauncher.Daemon.dll"]
