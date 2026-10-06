#!/usr/bin/env bash
set -Eeuo pipefail

readonly SOURCE_URL="https://github.com/mwinteringham/restful-booker-platform.git"
readonly SOURCE_SHA="d36bd3f8647a091d406e53bad463c5e3e5d2ece1"
readonly JAVA_IMAGE="eclipse-temurin:26-jre-alpine@sha256:9eedff2367194d11eddd6f14101b444945a708c986270cd5716b934596ba3a31"
readonly NODE_IMAGE="node:24.14.1@sha256:80fc934952c8f1b2b4d39907af7211f8a9fff1a4c2cf673fb49099292c251cec"
readonly MAVEN_VERSION="3.9.14"
readonly MAVEN_SHA512="d50af8ab5e6005b46a07f0ce9d3719e67cfdf898da988a84871304cd59fb1af0fef2f99dea709e6e66f21f732f905979b5c2dce6b6860406f60a70e84d9cf0b8"
fail() {
    printf 'UI provisioning error: %s\n' "$1" >&2
    exit 1
}

validate_compose_model() {
    local model_file="$1"
    [[ -f "$model_file" ]] || fail "merged Compose model is missing"

    node - "$model_file" <<'NODE'
const fs = require("node:fs");

const fail = (reason) => {
  console.error(`Merged Compose model rejected: ${reason}`);
  process.exit(1);
};
let model;
try {
  model = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
} catch {
  fail("model is not valid JSON");
}

const expected = [
  "rbp-assets",
  "rbp-auth",
  "rbp-booking",
  "rbp-branding",
  "rbp-message",
  "rbp-report",
  "rbp-room",
].sort();
const services = model?.services;
if (!services || JSON.stringify(Object.keys(services).sort()) !== JSON.stringify(expected)) {
  fail("service set differs from the pinned platform");
}

for (const [name, service] of Object.entries(services)) {
  const ports = service.ports ?? [];
  if (service.network_mode) fail(`custom network mode is not allowed for ${name}`);
  if (!Array.isArray(ports)) fail(`invalid port configuration for ${name}`);
  if (name !== "rbp-assets" && ports.length !== 0) {
    fail(`unexpected published ports for ${name}`);
  }
  if (name === "rbp-assets") {
    const port = ports[0];
    if (
      ports.length !== 1 ||
      !port ||
      typeof port !== "object" ||
      Number(port.target) !== 80 ||
      String(port.published) !== "0" ||
      port.host_ip !== "127.0.0.1" ||
      (port.protocol ?? "tcp") !== "tcp"
    ) {
      fail("frontend must publish only target 80 on an ephemeral loopback port");
    }
  }
}
NODE
}

summarize_compose_status() {
    node -e '
const fs = require("node:fs");
const services = ["rbp-assets", "rbp-auth", "rbp-booking", "rbp-branding", "rbp-message", "rbp-report", "rbp-room"];
const states = new Set(["created", "restarting", "running", "removing", "paused", "exited", "dead"]);
const raw = fs.readFileSync(0, "utf8").trim();
if (!raw) {
  console.log("compose_status=unavailable");
  process.exit(0);
}
try {
  let rows;
  try {
    const parsed = JSON.parse(raw);
    rows = Array.isArray(parsed) ? parsed : [parsed];
  } catch {
    rows = raw.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line));
  }
  for (const service of services) {
    const row = rows.find((item) => item?.Service === service);
    const state = states.has(String(row?.State).toLowerCase()) ? String(row.State).toLowerCase() : "unknown";
    console.log(`${service}=${state}`);
  }
} catch {
  console.log("compose_status=unavailable");
}
'
}

pin_dockerfiles() {
    local source_dir="$1"
    [[ -d "$source_dir" ]] || fail "pinned source directory is missing"

    node - "$source_dir" "$JAVA_IMAGE" "$NODE_IMAGE" <<'NODE'
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(process.argv[2]);
const javaImage = process.argv[3];
const nodeImage = process.argv[4];
const fail = (reason) => {
  console.error(`Pinned Dockerfile check failed: ${reason}`);
  process.exit(1);
};
const expectedFiles = [
  "assets/Dockerfile",
  "auth/Dockerfile",
  "booking/Dockerfile",
  "branding/Dockerfile",
  "message/Dockerfile",
  "report/Dockerfile",
  "room/Dockerfile",
].sort();
const foundFiles = [];
const visit = (directory) => {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.isSymbolicLink()) fail("symbolic links are not allowed in Dockerfile inputs");
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) visit(fullPath);
    else if (entry.name === "Dockerfile") foundFiles.push(path.relative(root, fullPath).split(path.sep).join("/"));
  }
};
visit(root);
foundFiles.sort();
if (JSON.stringify(foundFiles) !== JSON.stringify(expectedFiles)) {
  fail("Dockerfile set differs from the pinned platform");
}

const rewrites = [];
const aliasPattern = /^FROM\s+(?:--platform=\S+\s+)?(\S+)(?:\s+AS\s+(\S+))?/i;
for (const relativePath of expectedFiles) {
  const file = path.join(root, relativePath);
  const original = fs.readFileSync(file, "utf8");
  const lines = original.split(/(?<=\n)/);
  let javaCount = 0;
  let nodeCount = 0;
  const rewritten = lines.map((line) => {
    if (/^FROM\s+eclipse-temurin:26-jre-alpine\s*$/i.test(line.trim())) {
      javaCount += 1;
      return line.replace("eclipse-temurin:26-jre-alpine", javaImage);
    }
    if (/^FROM\s+node:24\s+AS\s+base\s*$/i.test(line.trim())) {
      nodeCount += 1;
      return line.replace("node:24", nodeImage);
    }
    return line;
  }).join("");

  const expectedCount = relativePath === "assets/Dockerfile" ? nodeCount === 1 && javaCount === 0 : javaCount === 1 && nodeCount === 0;
  if (!expectedCount) fail(`unexpected base-image reference in ${relativePath}`);

  const aliases = new Set();
  for (const line of rewritten.split(/\r?\n/)) {
    const match = line.match(aliasPattern);
    if (!match) continue;
    const [, image, alias] = match;
    if (!aliases.has(image) && image !== javaImage && image !== nodeImage) {
      fail(`unexpected external base image in ${relativePath}`);
    }
    if (alias) aliases.add(alias.toLowerCase());
  }
  rewrites.push({ file, rewritten });
}

for (const item of rewrites) {
  const temporary = `${item.file}.pin-tmp`;
  fs.writeFileSync(temporary, item.rewritten, { flag: "wx" });
  fs.renameSync(temporary, item.file);
}
NODE
}

run_bounded() {
    local remaining=$((1200 - SECONDS))
    (( remaining > 0 )) || return 124
    timeout --foreground --kill-after=2s "${remaining}s" "$@"
}

write_diagnostics() {
    local destination="$WORK_DIR/provisioning-diagnostics.txt"
    {
        printf 'stage=%s\n' "$CURRENT_STAGE"
        printf 'source_sha=%s\n' "$SOURCE_SHA"
        printf 'readiness=%s\n' "$PROBE_SUMMARY"
        if [[ "$COMPOSE_CONFIGURED" == "true" ]]; then
            local status_json
            status_json="$(docker compose --project-name "$PROJECT_NAME" \
                --file "$SOURCE_DIR/docker-compose.yml" \
                --file "$OVERRIDE_FILE" ps --format json 2>/dev/null || true)"
            printf '%s' "$status_json" | summarize_compose_status >> "$destination"
        fi
    } > "$destination"
    printf 'Safe provisioning summary: %s\n' "$destination" >&2
}

on_exit() {
    local result=$?
    trap - EXIT
    if (( result != 0 )); then
        printf 'UI provisioning failed during %s.\n' "$CURRENT_STAGE" >&2
        if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then
            write_diagnostics || printf 'Safe provisioning summary could not be written.\n' >&2
        fi
        if [[ -n "$PROJECT_NAME" && "$COMPOSE_CONFIGURED" == "true" ]]; then
            docker compose --project-name "$PROJECT_NAME" \
                --file "$SOURCE_DIR/docker-compose.yml" \
                --file "$OVERRIDE_FILE" down --volumes --remove-orphans --timeout 30 >/dev/null 2>&1 ||
                printf 'Scoped cleanup attempt failed for this Compose project.\n' >&2
        fi
    fi
    exit "$result"
}

main() {
    local mode="${1:-}"
    case "$mode" in
        --validate-compose-model)
            [[ $# -eq 2 ]] || fail "usage: provision-ui.sh --validate-compose-model <json-file>"
            validate_compose_model "$2"
            return
            ;;
        --summarize-compose-status)
            [[ $# -eq 2 && -f "$2" ]] || fail "usage: provision-ui.sh --summarize-compose-status <json-file>"
            cat "$2" | summarize_compose_status
            return
            ;;
        --pin-dockerfiles)
            [[ $# -eq 2 ]] || fail "usage: provision-ui.sh --pin-dockerfiles <source-directory>"
            pin_dockerfiles "$2"
            return
            ;;
        "")
            ;;
        *)
            fail "unknown option"
            ;;
    esac

    local required_commands=(curl docker git node sha512sum tar timeout)
    for command_name in "${required_commands[@]}"; do
        command -v "$command_name" >/dev/null 2>&1 || fail "required command is missing: $command_name"
    done
    [[ -n "${JAVA_HOME:-}" && -x "$JAVA_HOME/bin/javac" ]] || fail "JDK 26 must be installed and JAVA_HOME must point to it"
    [[ "$(node --version 2>/dev/null)" == "v24.14.1" ]] || fail "Node.js 24.14.1 is required"
    [[ "$(npm --version 2>/dev/null)" == "11.11.0" ]] || fail "npm 11.11.0 is required"
    local java_version compiler_version
    java_version="$(java -version 2>&1 | head -n 1)"
    compiler_version="$(javac -version 2>&1)"
    [[ "$java_version" == *'"26.'* || "$java_version" == *'"26"'* ]] || fail "JDK 26 is required"
    [[ "$compiler_version" == "javac 26."* || "$compiler_version" == "javac 26" ]] ||
        fail "JDK compiler 26 is required"

    local compose_version
    compose_version="$(docker compose version --short 2>/dev/null | sed 's/^v//')"
    [[ "$compose_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "Docker Compose version could not be verified"
    local compose_major compose_minor
    IFS=. read -r compose_major compose_minor _ <<< "$compose_version"
    (( compose_major > 2 || (compose_major == 2 && compose_minor >= 24) )) ||
        fail "Docker Compose 2.24.0 or later is required for !override"

    [[ -n "${GITHUB_RUN_ID:-}" && "$GITHUB_RUN_ID" =~ ^[0-9]+$ ]] || fail "GitHub run ID is unavailable"
    local attempt="${GITHUB_RUN_ATTEMPT:-1}"
    [[ "$attempt" =~ ^[0-9]+$ ]] || fail "GitHub run attempt is invalid"
    PROJECT_NAME="restbroker-ui-${GITHUB_RUN_ID}-${attempt}"
    [[ "$PROJECT_NAME" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || fail "generated Compose project name is invalid"
    [[ -n "${RUNNER_TEMP:-}" && -d "$RUNNER_TEMP" ]] || fail "runner temporary directory is unavailable"
    WORK_DIR="$RUNNER_TEMP/$PROJECT_NAME"
    [[ ! -e "$WORK_DIR" ]] || fail "run-scoped temporary directory already exists"
    mkdir -m 700 "$WORK_DIR"
    SOURCE_DIR="$WORK_DIR/source"
    OVERRIDE_FILE="$WORK_DIR/compose.override.yml"
    COMPOSE_CONFIGURED="false"
    PROBE_SUMMARY="not-run"
    CURRENT_STAGE="source checkout"
    trap on_exit EXIT
    SECONDS=0

    run_bounded git init --quiet "$SOURCE_DIR" >>"$WORK_DIR/command.log" 2>&1 ||
        fail "could not initialize pinned source checkout"
    run_bounded git -C "$SOURCE_DIR" remote add origin "$SOURCE_URL" >>"$WORK_DIR/command.log" 2>&1 ||
        fail "could not configure pinned source origin"
    run_bounded git -C "$SOURCE_DIR" fetch --quiet --depth=1 --no-tags origin "$SOURCE_SHA" >>"$WORK_DIR/command.log" 2>&1 ||
        fail "could not fetch pinned source revision"
    run_bounded git -C "$SOURCE_DIR" checkout --quiet --detach FETCH_HEAD >>"$WORK_DIR/command.log" 2>&1 ||
        fail "could not check out pinned source revision"
    [[ "$(git -C "$SOURCE_DIR" rev-parse HEAD)" == "$SOURCE_SHA" ]] ||
        fail "checked out source revision does not match the approved SHA"

    CURRENT_STAGE="toolchain setup"
    local maven_archive="$WORK_DIR/apache-maven-$MAVEN_VERSION-bin.tar.gz"
    run_bounded curl --fail --silent --show-error --location --retry 3 \
        --output "$maven_archive" \
        "https://archive.apache.org/dist/maven/maven-3/$MAVEN_VERSION/binaries/apache-maven-$MAVEN_VERSION-bin.tar.gz" \
        >>"$WORK_DIR/command.log" 2>&1 || fail "Maven archive download failed"
    printf '%s  %s\n' "$MAVEN_SHA512" "$maven_archive" | sha512sum --check --status ||
        fail "Maven archive SHA-512 verification failed"
    mkdir "$WORK_DIR/tools"
    run_bounded tar -xzf "$maven_archive" -C "$WORK_DIR/tools" >>"$WORK_DIR/command.log" 2>&1 ||
        fail "Maven archive extraction failed"
    local maven="$WORK_DIR/tools/apache-maven-$MAVEN_VERSION/bin/mvn"

    CURRENT_STAGE="base-image pinning"
    pin_dockerfiles "$SOURCE_DIR"

    CURRENT_STAGE="upstream Maven build"
    run_bounded "$maven" --batch-mode --no-transfer-progress -f "$SOURCE_DIR/pom.xml" clean install -DskipTests \
        >>"$WORK_DIR/command.log" 2>&1 || fail "pinned source Maven build failed"

    cat > "$OVERRIDE_FILE" <<'YAML'
services:
  rbp-booking:
    ports: !override []
  rbp-room:
    ports: !override []
  rbp-branding:
    ports: !override []
  rbp-assets:
    ports: !override
      - target: 80
        published: "0"
        host_ip: 127.0.0.1
        protocol: tcp
  rbp-auth:
    ports: !override []
  rbp-report:
    ports: !override []
  rbp-message:
    ports: !override []
YAML

    CURRENT_STAGE="merged Compose configuration validation"
    local -a compose_args=(docker compose --project-name "$PROJECT_NAME"
        --file "$SOURCE_DIR/docker-compose.yml" --file "$OVERRIDE_FILE")
    run_bounded "${compose_args[@]}" config --format json > "$WORK_DIR/compose-model.json" 2>>"$WORK_DIR/command.log" ||
        fail "Compose configuration could not be rendered"
    validate_compose_model "$WORK_DIR/compose-model.json"
    COMPOSE_CONFIGURED="true"

    CURRENT_STAGE="platform build and startup"
    run_bounded "${compose_args[@]}" up --build --detach >>"$WORK_DIR/command.log" 2>&1 ||
        fail "pinned UI platform build or startup failed"

    CURRENT_STAGE="frontend port discovery"
    local published_port
    published_port="$(run_bounded "${compose_args[@]}" port --index 1 rbp-assets 80 2>>"$WORK_DIR/command.log")" ||
        fail "frontend port could not be discovered"
    [[ "$published_port" =~ ^127\.0\.0\.1:([0-9]{1,5})$ ]] ||
        fail "frontend port is not bound to loopback"
    local frontend_url="http://127.0.0.1:${BASH_REMATCH[1]}"

    CURRENT_STAGE="readiness"
    local probe_script
    probe_script='const safeCode=e=>{const c=String(e.cause?.code||"");return /^[A-Z0-9_]{1,32}$/.test(c)?c:"NETWORK_ERROR"}; const endpoints=[["booking","http://rbp-booking:3000/booking/actuator/health"],["room","http://rbp-room:3001/room/actuator/health"],["branding","http://rbp-branding:3002/branding/actuator/health"],["auth","http://rbp-auth:3004/auth/actuator/health"],["report","http://rbp-report:3005/report/actuator/health"],["message","http://rbp-message:3006/message/actuator/health"]]; Promise.all(endpoints.map(async([name,url])=>{try{const response=await fetch(url,{signal:AbortSignal.timeout(3000)});let body;try{body=await response.json()}catch{}return{name,result:`${response.status}:${body?.status==="UP"?"UP":"DOWN"}`,ok:response.status===200&&body?.status==="UP"}}catch(error){return{name,result:safeCode(error),ok:false}}})).then(results=>{console.log(results.map(({name,result})=>`${name}=${result}`).join(" "));if(results.some(({ok})=>!ok))process.exitCode=1})'
    local frontend_script
    frontend_script='const safeCode=e=>{const c=String(e.cause?.code||"");return /^[A-Z0-9_]{1,32}$/.test(c)?c:"NETWORK_ERROR"}; fetch(process.argv[1],{signal:AbortSignal.timeout(3000)}).then(response=>{console.log(`frontend=${response.status}`);if(!response.ok)process.exitCode=1}).catch(error=>{console.log(`frontend=${safeCode(error)}`);process.exitCode=1})'
    local network="${PROJECT_NAME}_default"
    local probe
    local ready="false"
    while (( SECONDS < 1200 )); do
        probe="$(run_bounded docker run --rm --network "$network" "$NODE_IMAGE" node -e "$probe_script" 2>/dev/null || true)"
        local frontend
        frontend="$(run_bounded node -e "$frontend_script" "$frontend_url" 2>/dev/null || true)"
        PROBE_SUMMARY="${probe:-probe-unavailable}; ${frontend:-frontend-unavailable}"
        if [[ "$probe" == *"booking=200:UP"* &&
              "$probe" == *"room=200:UP"* &&
              "$probe" == *"branding=200:UP"* &&
              "$probe" == *"auth=200:UP"* &&
              "$probe" == *"report=200:UP"* &&
              "$probe" == *"message=200:UP"* &&
              "$frontend" == "frontend=200" ]]; then
            ready="true"
            break
        fi
        sleep 5
    done
    [[ "$ready" == "true" ]] ||
        fail "private UI readiness did not succeed before the 20-minute bound"

    if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
        {
            printf 'ui-base-url=%s\n' "$frontend_url"
            printf 'ui-compose-project=%s\n' "$PROJECT_NAME"
            printf 'ui-compose-file=%s\n' "$SOURCE_DIR/docker-compose.yml"
            printf 'ui-compose-override=%s\n' "$OVERRIDE_FILE"
            printf 'ui-diagnostics-path=%s\n' "$WORK_DIR/provisioning-diagnostics.txt"
        } >> "$GITHUB_OUTPUT"
    fi
    printf 'ui-base-url=%s\n' "$frontend_url"
    printf 'ui-compose-project=%s\n' "$PROJECT_NAME"
    printf 'ui-compose-file=%s\n' "$SOURCE_DIR/docker-compose.yml"
    printf 'ui-compose-override=%s\n' "$OVERRIDE_FILE"
    printf 'ui-diagnostics-path=%s\n' "$WORK_DIR/provisioning-diagnostics.txt"
    trap - EXIT
}

PROJECT_NAME=""
SOURCE_DIR=""
OVERRIDE_FILE=""
WORK_DIR=""
COMPOSE_CONFIGURED="false"
CURRENT_STAGE="preflight"
PROBE_SUMMARY="not-run"
main "$@"
