# syntax=docker/dockerfile:1

FROM node:22-alpine AS frontend
WORKDIR /build
COPY package.json yarn.lock ./
RUN corepack enable && yarn install --frozen-lockfile
COPY frontend ./frontend
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG VERSION=0.0.0-dev
WORKDIR /build
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src ./src
RUN dotnet publish src/Portarr.Host/Portarr.Host.csproj -c Release -o /app -p:Version=${VERSION} \
    && mkdir /config

# Chiseled image: no shell, no package manager, non-root user by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /build/_output/UI ./wwwroot
# The chiseled image has no shell to create folders: copy an empty one owned by the app user.
COPY --from=backend --chown=1654:1654 /config /config
ENV PORTARR__DATAFOLDER=/config
# The app user of the chiseled image, stated so that nothing reads this image as running as root.
USER 1654:1654
VOLUME /config
EXPOSE 8484
ENTRYPOINT ["dotnet", "Portarr.dll"]
