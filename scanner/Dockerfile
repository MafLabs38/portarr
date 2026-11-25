# portarr-scanner (D-131): the optional companion that runs the Trivy CLI on the images Portarr names, one at a time.
# Not part of Portarr's image. It only pulls image layers from registries and reads release SBOMs from GitHub: nothing runs on your machines.
#
#   docker build -t portarr-scanner scanner/
#
# Node 22 runs the TypeScript sources directly (type stripping): no build step, no runtime dependency.
# Both base images are pinned by version and digest (Dependabot keeps them current): Trivy is the program that reads every layer.
FROM aquasec/trivy:0.76.2@sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb AS trivy

FROM node:22.18-alpine@sha256:1b2479dd35a99687d6638f5976fd235e26c5b37e8122f786fcd5fe231d63de5b
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
