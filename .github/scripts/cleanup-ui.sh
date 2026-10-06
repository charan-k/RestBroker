#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 3 ]]; then
    printf 'Usage: cleanup-ui.sh <compose-project> <compose-file> <override-file>\n' >&2
    exit 2
fi

project_name="$1"
compose_file="$2"
override_file="$3"

if [[ -z "${GITHUB_RUN_ID:-}" || -z "${RUNNER_TEMP:-}" ]]; then
    printf 'Refusing cleanup: GitHub run context is unavailable.\n' >&2
    exit 2
fi
attempt="${GITHUB_RUN_ATTEMPT:-1}"
expected_project="restbroker-ui-${GITHUB_RUN_ID}-${attempt}"
if [[ ! "$GITHUB_RUN_ID" =~ ^[0-9]+$ || ! "$attempt" =~ ^[0-9]+$ || "$project_name" != "$expected_project" ]]; then
    printf 'Refusing cleanup: Compose project does not match this workflow run.\n' >&2
    exit 2
fi
expected_directory="${RUNNER_TEMP%/}/$expected_project"
if [[ "$compose_file" != "$expected_directory/source/docker-compose.yml" ||
      "$override_file" != "$expected_directory/compose.override.yml" ]]; then
    printf 'Refusing cleanup: Compose files are outside this workflow run directory.\n' >&2
    exit 2
fi
if [[ ! -f "$compose_file" || ! -f "$override_file" ]]; then
    printf 'Refusing cleanup: run-scoped Compose files are missing.\n' >&2
    exit 2
fi
if ! command -v docker >/dev/null 2>&1; then
    printf 'Cleanup failed: Docker is unavailable.\n' >&2
    exit 1
fi

docker compose --project-name "$project_name" \
    --file "$compose_file" \
    --file "$override_file" \
    down --volumes --remove-orphans --timeout 30
