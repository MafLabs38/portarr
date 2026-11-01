# Backrest with the database clients the dump hooks need. The stock image has neither pg_dump nor mariadb-dump;
# dumping over the network keeps the Docker socket (root on the host) out of the backup container.
# Pin the versions; Portarr's connector targets Backrest v1.14.x. This container holds the password of every repository, so the
# base image is pinned by digest (the multi-arch index of v1.14.1). To move to another release: docker buildx imagetools inspect
# garethgeorge/backrest:<tag> prints the digest, then change the line below or pass --build-arg BACKREST_IMAGE=...@sha256:<digest>.
ARG BACKREST_IMAGE=garethgeorge/backrest:v1.14.1@sha256:b852979754281026230cc69fb11428e6d57c9a97784ab4a444ffc7934c53a215
FROM ${BACKREST_IMAGE}

# The stock image runs as root already, and the database clients are installed by root: this line states that, it does not
# raise anything. apk resolves the clients of the Alpine release the base image is built on, so they follow its version.
USER root
RUN apk add --no-cache postgresql17-client mariadb-client
