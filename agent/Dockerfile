# portarr-agent (D-87): the optional companion that runs an agent CLI headless behind Portarr's chat panel.
# Not part of Portarr's image. Its only tool is Portarr's MCP server: no shell tool, no access to the machines.
#
#   docker build -t portarr-agent agent/
#
# Node 22 runs the TypeScript sources directly (type stripping): no build step, no runtime dependency.
FROM node:22.18-bookworm-slim

# The agent CLIs. Pin the version you validated; adapters for other CLIs add theirs here.
ARG CLAUDE_CODE_VERSION=2.1.63
RUN npm install -g "@anthropic-ai/claude-code@${CLAUDE_CODE_VERSION}" \
 && npm cache clean --force

WORKDIR /app
COPY package.json ./
COPY src ./src

# Conversations, the CLI's own state (CLAUDE_CONFIG_DIR) and its empty working directory.
RUN mkdir -p /data && chown node:node /data
VOLUME /data
USER node

ENV PORTARR_AGENT_DATA=/data \
    PORTARR_AGENT_PORT=8488 \
    NODE_ENV=production
EXPOSE 8488

HEALTHCHECK --interval=30s --timeout=3s CMD node -e "fetch('http://127.0.0.1:8488/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

CMD ["node", "src/main.ts"]
