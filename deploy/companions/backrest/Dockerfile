# Backrest with the database clients the dump hooks need. The stock image has neither pg_dump nor mariadb-dump;
# dumping over the network keeps the Docker socket (root on the host) out of the backup container.
# Pin the versions; Portarr's connector targets Backrest v1.14.x. This container holds the password of every repository:
# pin the base image by digest once you have chosen the version (docker buildx imagetools inspect garethgeorge/backrest:v1.14.1
# prints it), then pass it with --build-arg BACKREST_IMAGE=garethgeorge/backrest@sha256:<digest>.
ARG BACKREST_IMAGE=garethgeorge/backrest:v1.14.1
FROM ${BACKREST_IMAGE}

# The stock image runs as root already, and the database clients are installed by root: this line states that, it does not
# raise anything. apk resolves the clients of the Alpine release the base image is built on, so they follow its version.
USER root
RUN apk add --no-cache postgresql17-client mariadb-client
