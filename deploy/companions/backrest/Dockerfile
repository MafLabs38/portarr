# Backrest with the database clients the dump hooks need. The stock image has neither pg_dump nor mariadb-dump;
# dumping over the network keeps the Docker socket (root on the host) out of the backup container.
# Pin the versions; Portarr's connector targets Backrest v1.14.x.
FROM garethgeorge/backrest:v1.14.1
USER root
RUN apk add --no-cache postgresql17-client mariadb-client
