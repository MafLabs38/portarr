#!/usr/bin/env bash
# Smoke test of the reader as it ships (native AOT, D-102): what only a native build can get wrong (reflection, JSON,
# configuration binding) and what the in-process tests cannot see. Run in CI against the container built from the Dockerfile.
#
#   tests/reader/smoke.sh <base-url> <token> [container-name]
#
# With a container name, the per-container routes are checked against that container too.
set -euo pipefail

BASE=${1:?usage: smoke.sh <base-url> <token> [container-name]}
TOKEN=${2:?usage: smoke.sh <base-url> <token> [container-name]}
CONTAINER=${3:-}
AUTH="Authorization: Bearer ${TOKEN}"

fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { echo "ok: $*"; }
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
has() { grep -qF -- "$2" <<<"$1" || fail "$3 (missing: $2)"; }
hasnt() { ! grep -qF -- "$2" <<<"$1" || fail "$3 (must not contain: $2)"; }

# Start-up can take a moment in a fresh container.
for _ in $(seq 1 30); do curl -fs "$BASE/healthz" >/dev/null 2>&1 && break; sleep 1; done
[[ "$(curl -s "$BASE/healthz")" == ok ]] || fail "/healthz is not ok"
pass "/healthz answers without a token"

[[ "$(status "$BASE/v1/info")" == 401 ]] || fail "no token must be refused (401)"
[[ "$(status -H "Authorization: Bearer wrong" "$BASE/v1/host")" == 401 ]] || fail "a wrong token must be refused (401)"
pass "the token is required"

info=$(curl -fs -H "$AUTH" "$BASE/v1/info") || fail "/v1/info failed (native JSON?)"
has "$info" '"name":"portarr-reader"' "/v1/info name"
has "$info" '"capabilities":["host","docker"]' "/v1/info capabilities"
pass "/v1/info (serialized without reflection)"

host=$(curl -fs -H "$AUTH" "$BASE/v1/host") || fail "/v1/host failed"
for family in node_cpu_seconds_total node_memory_MemTotal_bytes node_memory_MemAvailable_bytes node_load1; do has "$host" "$family" "/v1/host"; done
pass "/v1/host speaks the node_exporter format"

dockerinfo=$(curl -fs -H "$AUTH" "$BASE/v1/docker/info") || fail "/v1/docker/info failed (is the Docker socket mounted?)"
has "$dockerinfo" '"NCPU"' "docker info"
has "$dockerinfo" '"MemTotal"' "docker info"
hasnt "$dockerinfo" '"SecurityOptions"' "docker info must be reduced"
list=$(curl -fs -H "$AUTH" "$BASE/v1/docker/containers/json?all=true") || fail "container list failed"
[[ "${list:0:1}" == "[" ]] || fail "the container list is not a JSON array"
hasnt "$list" '"Command"' "the container list must lose the fields Portarr does not read"
pass "Docker info and container list (filtered)"

if [[ -n "$CONTAINER" ]]; then
  has "$list" "\"/${CONTAINER}\"" "the container list must hold ${CONTAINER}"
  inspect=$(curl -fs -H "$AUTH" "$BASE/v1/docker/containers/${CONTAINER}/json") || fail "inspect failed"
  has "$inspect" '"Id"' "inspect"
  has "$inspect" '"Tty"' "inspect"
  hasnt "$inspect" '"Env"' "inspect must never carry the environment"
  stats=$(curl -fs -H "$AUTH" "$BASE/v1/docker/containers/${CONTAINER}/stats?one-shot=true") || fail "stats failed"
  has "$stats" '"memory_stats"' "stats"
  [[ "$(status -H "$AUTH" "$BASE/v1/docker/containers/${CONTAINER}/logs?tail=1")" == 200 ]] || fail "logs failed"
  pass "inspect (no environment), one-shot stats and logs of ${CONTAINER}"
fi

[[ "$(status -X POST -H "$AUTH" "$BASE/v1/docker/containers/json")" == 405 ]] || fail "POST must be refused (405)"
[[ "$(status -H "$AUTH" "$BASE/v1/docker/version")" == 404 ]] || fail "an unlisted Docker path must have no route (404)"
pass "nothing but the allowlisted GET routes"
echo "reader smoke test passed"
