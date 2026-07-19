# syntax=docker/dockerfile:1.11

ARG DOTNET_SDK_VERSION=10.0.302
ARG DOTNET_RUNTIME_VERSION=10.0.10

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION}-noble AS build
WORKDIR /source
COPY . .
ARG PROJECT=src/Part07_DistributedComm/Part07_DistributedComm.csproj
RUN dotnet restore "${PROJECT}" \
    && dotnet publish "${PROJECT}" \
        --configuration Release \
        --no-restore \
        --output /out/app \
        /p:UseAppHost=false \
    && dotnet publish src/Campus.HealthProbe/Campus.HealthProbe.csproj \
        --configuration Release \
        --output /out/health \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION}-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build --chown=1654:1654 /out/app/ ./
COPY --from=build --chown=1654:1654 /out/health/ /health/
USER 1654
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=4s --start-period=15s --retries=5 \
  CMD ["dotnet", "/health/Campus.HealthProbe.dll", "http://127.0.0.1:8080/health/live"]
ENTRYPOINT ["dotnet"]
