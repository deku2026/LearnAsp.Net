# syntax=docker/dockerfile:1
# W9 Part11_2 Native AOT image. Linux builder with the native toolchain,
# publishes linux-x64, final image is non-root chiseled runtime-deps with no
# SDK/JIT/runtime. Health probe is external (the binary has no shell).

ARG SDK=10.0-alpine
ARG RUNTIME=mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine-extra

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${SDK} AS build
WORKDIR /src
COPY src/Part11_2_NativeAotTrim/ ./Part11_2_NativeAotTrim/
COPY src/Part11_2_NativeAotTrim.Endpoints/ ./Part11_2_NativeAotTrim.Endpoints/
COPY Directory.Build.props Directory.Packages.props ./
RUN dotnet publish src/Part11_2_NativeAotTrim/Part11_2_NativeAotTrim.csproj \
    -c Release -r linux-x64 -o /app \
    /p:PublishAot=true /p:SelfContained=true

FROM ${RUNTIME} AS final
WORKDIR /app
COPY --from=build /app/Part11_2_NativeAotTrim .
USER $APP_UID
ENTRYPOINT ["./Part11_2_NativeAotTrim"]
