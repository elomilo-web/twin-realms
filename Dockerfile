# Twin Realms online server (Railway, or any Docker host).
# Builds the C# scripts, imports the assets once, then runs the game headless as the online server:
#   godot --headless --path . -- --server
# It listens for WebSocket connections on $PORT (Railway sets it) and keeps accounts and saves in $TR_DATA.
# Railway: attach a Volume mounted at /data, or every redeploy starts with no accounts. See docs/ONLINE.md.
ARG GODOT_VERSION=4.7.2

# ---- Godot .NET for Linux (busybox has unzip)
FROM busybox:1.36 AS godot
ARG GODOT_VERSION
ADD https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip /godot.zip
RUN unzip -q /godot.zip -d /opt \
 && mv /opt/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64 /opt/godot \
 && mv /opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64 /opt/godot/godot

# ---- build the scripts and import the assets
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY --from=godot /opt/godot /opt/godot
WORKDIR /game
COPY . .
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN dotnet build \
 && /opt/godot/godot --headless --path . --import \
 && test -d .godot/imported

# ---- run (the .NET runtime is enough here)
FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY --from=build /opt/godot /opt/godot
COPY --from=build /game /game
WORKDIR /game
ENV TR_DATA=/data PORT=8080
EXPOSE 8080
CMD ["/opt/godot/godot", "--headless", "--path", ".", "--", "--server"]
