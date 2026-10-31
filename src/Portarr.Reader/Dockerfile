# syntax=docker/dockerfile:1
# portarr-reader (D-100, D-102): the optional, read-only reader of one machine. One token, a node_exporter-format /v1/host and
# a filtered Docker window at /v1/docker/. Compiled ahead of time (native AOT): one small binary and no .NET runtime in the
# image. Built from the repository root:
#
#   docker build -f src/Portarr.Reader/Dockerfile -t portarr-reader .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
ARG VERSION=0.0.0-dev
WORKDIR /build
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Portarr.Reader ./src/Portarr.Reader
RUN dotnet publish src/Portarr.Reader/Portarr.Reader.csproj -c Release -o /app -p:Version=${VERSION}

# Chiseled image with the native dependencies only: no shell, no package manager, no .NET runtime, non-root user (uid 1654)
# by default. The health check is GET /healthz (the image has no curl): wire it in your orchestrator.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/portarr-reader ./
ENV PORTARR_READER_URLS=http://+:8485
EXPOSE 8485
ENTRYPOINT ["./portarr-reader"]
