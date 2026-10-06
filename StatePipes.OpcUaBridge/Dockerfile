# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["StatePipes.OpcUaBridge/StatePipes.OpcUaBridge.csproj", "StatePipes.OpcUaBridge/"]
COPY ["StatePipes/StatePipes.csproj", "StatePipes/"]
RUN dotnet restore "./StatePipes.OpcUaBridge/StatePipes.OpcUaBridge.csproj"
# Copies StatePipes.BrokerProxy too: the bridge compiles its broker channels as linked source.
COPY . .
WORKDIR "/src/StatePipes.OpcUaBridge"
RUN dotnet build "./StatePipes.OpcUaBridge.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./StatePipes.OpcUaBridge.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
# /pki holds the OPC UA certificate stores: the bridge's own certificate is created there on first start, and
# server certificates are trusted by placing them in /pki/trusted/certs. Mount a volume so both survive restarts.
# /usr/share/StatePipes/StatePipes.OpcUaBridge is where StatePipes keeps its Config and Log, and where CERTPATH
# and PWPATH file names are looked up (Certs). Its directories are created here so mounting just Certs works.
USER root
RUN mkdir -p /pki /usr/share/StatePipes/StatePipes.OpcUaBridge/Config /usr/share/StatePipes/StatePipes.OpcUaBridge/Log /usr/share/StatePipes/StatePipes.OpcUaBridge/Certs     && chown -R $APP_UID /pki /usr/share/StatePipes
USER $APP_UID
ENV OPCUA_PKI_PATH=/pki
VOLUME /pki
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StatePipes.OpcUaBridge.dll"]
