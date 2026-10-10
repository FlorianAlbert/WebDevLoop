#!/usr/bin/env bash
# Starts WebDevLoop with an empty data directory, downloads its OpenAPI document and converts it to
# docs/rest-api/webdevloop.swagger.json (Swagger 2.0, the format DocFX renders).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
port="${OPENAPI_PORT:-5199}"
work="$(mktemp -d)"
log="$work/app.log"
app_pid=""

cleanup() {
  if [[ -n "$app_pid" ]]; then kill "$app_pid" 2>/dev/null || true; fi
  rm -rf "$work"
}
trap cleanup EXIT

dotnet build "$root/src/WebDevLoop.Web" -c Release --nologo -v q

# The app starts in diagnostic-only mode without GitHub sign-in; the OpenAPI document is served anyway.
WebDevLoop__DataDirectory="$work/data" ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project "$root/src/WebDevLoop.Web" -c Release --no-build --no-launch-profile \
  --urls "http://localhost:$port" >"$log" 2>&1 &
app_pid=$!

for _ in $(seq 1 60); do
  if curl -fs "http://localhost:$port/openapi/v1.json" -o "$work/openapi.json"; then break; fi
  if ! kill -0 "$app_pid" 2>/dev/null; then cat "$log"; echo "WebDevLoop stopped before serving OpenAPI" >&2; exit 1; fi
  sleep 1
done
[[ -s "$work/openapi.json" ]] || { cat "$log"; echo "No OpenAPI document received" >&2; exit 1; }

mkdir -p "$root/docs/rest-api"
dotnet run "$root/docs/tools/OpenApiToSwagger.cs" "$work/openapi.json" "$root/docs/rest-api/webdevloop.swagger.json"
echo "Wrote docs/rest-api/webdevloop.swagger.json"
