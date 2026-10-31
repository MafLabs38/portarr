# syntax=docker/dockerfile:1
# portarr-reader (D-100): the optional, read-only reader of one machine. One token, a node_exporter-format /v1/host and
# a filtered Docker window at /v1/docker/. Built from the repository root:
#
#   docker build -f src/Portarr.Reader/Dockerfile -t portarr-reader .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-dev
WORKDIR /build
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Portarr.Reader ./src/Portarr.Reader
RUN dotnet publish src/Portarr.Reader/Portarr.Reader.csproj -c Release -o /app -p:Version=${VERSION} -p:UseAppHost=false

# Chiseled image: no shell, no package manager, non-root user (uid 1654) by default. The health check is
# GET /healthz (the image has no curl): wire it in your orchestrator.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app ./
ENV PORTARR_READER_URLS=http://+:8485
EXPOSE 8485
ENTRYPOINT ["dotnet", "portarr-reader.dll"]
