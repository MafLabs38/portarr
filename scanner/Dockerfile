# portarr-scanner (D-131): the optional companion that runs the Trivy CLI on the images Portarr names, one at a time.
# Not part of Portarr's image. It only pulls image layers from registries: nothing runs on your machines.
#
#   docker build -t portarr-scanner scanner/
#
# Node 22 runs the TypeScript sources directly (type stripping): no build step, no runtime dependency.
# Both base images are pinned by version and digest (Dependabot keeps them current): Trivy is the program that reads every layer.
FROM aquasec/trivy:0.76.2@sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb AS trivy

FROM node:26.13-alpine@sha256:6bd9ab43c0ec0190ca7b2d193906cb8b7edf6e7f4afa43ddeccd6452710bffb5
COPY --from=trivy /usr/local/bin/trivy /usr/local/bin/trivy

WORKDIR /app
COPY package.json ./
COPY src ./src

# The results file and Trivy's cache (its database and the layers of the scan in progress).
RUN mkdir -p /data && chown node:node /data
VOLUME /data
USER node

ENV PORTARR_SCANNER_DATA=/data \
    PORTARR_SCANNER_PORT=8489 \
    NODE_ENV=production
EXPOSE 8489

HEALTHCHECK --interval=30s --timeout=3s CMD node -e "fetch('http://127.0.0.1:8489/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

CMD ["node", "src/main.ts"]
