# syntax=docker/dockerfile:1
# portarr-reader (D-100, D-102): the optional, read-only reader of one machine. One token, a node_exporter-format /v1/host and a filtered
# Docker window at /v1/docker/. A static Rust binary in an empty image: about 1 MB of memory. Built from the repository root:
#
#   docker build -f reader/Dockerfile -t portarr-reader .
FROM rust:1-alpine@sha256:7e9e2d068a107a2adafd19930011178d14a78326053cd4c3a9f7ad3fd049557d AS build
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY reader/Cargo.toml reader/Cargo.lock ./
COPY reader/src ./src
RUN PORTARR_READER_VERSION=${VERSION} cargo build --release --locked

# Nothing else in the image: no shell, no package manager, no libc. Non-root by default; it needs the group of the Docker socket
# to read it (docker run --group-add ...). The health check is the binary itself: the image has no curl.
FROM scratch
COPY --from=build /src/target/release/portarr-reader /portarr-reader
USER 1654:1654
ENV PORTARR_READER_URLS=http://0.0.0.0:8485
EXPOSE 8485
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s CMD ["/portarr-reader", "--healthcheck"]
ENTRYPOINT ["/portarr-reader"]
