#!/usr/bin/env bash
# Checks that a published tacit folder runs: the version, the service, the management API, the MCP tools, backup and
# restore, the CLI's own calls to the service and the web interface.
#
#   build/smoke.sh <publish-dir> [<expected-version>]
#
# Uses a throwaway TACIT_HOME and a free port, so it never touches the user's memory or a running service. It runs init,
# starts serve in the background, waits for it, then reads and writes through every kind of request: the management
# API's reads and saves, a page and an activity written and read back through MCP, a reindex, a backup and the preview
# of restoring it, and tacit status. The release is trimmed, so this is where a part it lost would show. It stops the
# service and removes the throwaway folder whatever happened. No auxiliary model is configured. Without an expected
# version, any non-empty one passes. Exits non-zero on the first failed check, after printing the service's output and
# log. Needs bash 3.2 or later, curl and jq.
set -euo pipefail

if [ $# -lt 1 ] || [ $# -gt 2 ]; then
    echo "usage: build/smoke.sh <publish-dir> [<expected-version>]" >&2
    exit 2
fi
publish=$(cd "$1" && pwd)
expected=${2:-}

windows=false
if command -v cygpath > /dev/null; then windows=true; fi
# tacit reads TACIT_HOME itself, so on Windows it must be a path Windows reads.
native() { if $windows; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }

tacit=$publish/tacit
if $windows; then tacit=$publish/tacit.exe; fi
work=$(mktemp -d "${TMPDIR:-/tmp}/tacit-smoke.XXXXXX")
tacit_home=$work/home
TACIT_HOME=$(native "$tacit_home")
export TACIT_HOME
service=
passed=false

finish() {
    if [ -n "$service" ] && kill -0 "$service" 2> /dev/null; then
        kill "$service" 2> /dev/null || true
        wait "$service" 2> /dev/null || true
    fi
    if ! $passed; then
        for file in "$work/serve.out" "$work/serve.err" "$tacit_home"/logs/*.log; do
            if [ -f "$file" ]; then
                echo "---- $file"
                cat "$file"
            fi
        done
    fi
    rm -rf "$work"
}
trap finish EXIT

fail() {
    echo "Smoke check failed: $1" >&2
    exit 1
}

# jq on Windows may end lines with CR LF, and so may tacit's own output.
jq_value() { jq -r "$@" | tr -d '\r'; }

version=$("$tacit" --version | tr -d '\r') || fail "tacit --version exited with $?"
[ -n "$version" ] || fail "tacit --version printed nothing"
if [ -n "$expected" ] && [ "$version" != "$expected" ]; then
    fail "tacit --version printed '$version', expected '$expected'"
fi
echo "version: $version"

"$tacit" init --language en > /dev/null || fail "tacit init exited with $?"

# A port nothing answers on: curl's exit code 7 is a refused connection.
port=
for _ in 1 2 3 4 5 6 7 8 9 10; do
    candidate=$((20000 + RANDOM % 40000))
    set +e
    curl -s -o /dev/null --max-time 1 "http://127.0.0.1:$candidate/"
    refused=$?
    set -e
    if [ "$refused" -eq 7 ]; then
        port=$candidate
        break
    fi
done
[ -n "$port" ] || fail "found no free port"
printf 'server:\n  port: %s\n' "$port" >> "$tacit_home/config.yaml"

"$tacit" serve > "$work/serve.out" 2> "$work/serve.err" &
service=$!
base=http://127.0.0.1:$port
status=
for _ in $(seq 120); do
    kill -0 "$service" 2> /dev/null || fail "tacit serve exited before it answered"
    if status=$(curl -sf --max-time 2 "$base/api/status"); then break; fi
    status=
    sleep 0.25
done
[ -n "$status" ] || fail "tacit serve did not answer within 30 seconds"

[ "$(jq_value '.service.running' <<< "$status")" = true ] || fail "/api/status does not report the service as running"
reported=$(jq_value '.service.version' <<< "$status")
[ "$reported" = "$version" ] || fail "/api/status reports version '$reported', the executable '$version'"

# The release is trimmed and reads JSON only through generated contracts, so every kind of request and answer is
# exercised once: a type that went missing fails here rather than for a user.
api() {
    local method=$1 path=$2 body=${3:-} answer code
    if [ -n "$body" ]; then
        answer=$(curl -sS -X "$method" -H 'Content-Type: application/json' --data "$body" -w '\n%{http_code}' "$base/api/$path")
    else
        answer=$(curl -sS -X "$method" -w '\n%{http_code}' "$base/api/$path")
    fi
    code=${answer##*$'\n'}
    answer=${answer%$'\n'*}
    [ "$code" -lt 400 ] || fail "$method /api/$path failed with $code: $answer"
    printf '%s\n' "$answer"
}

api GET activity > /dev/null
api GET bindings > /dev/null
api GET backups > /dev/null
api GET openapi.json > /dev/null
today=$(api GET logs | jq_value '.today')
api GET "logs/$today" > /dev/null
config=$(api GET config)
api PUT config "$(jq -c '{hash: .hash, config: .file}' <<< "$config")" > /dev/null
resident=$(api GET prompts | jq -c '.documents[] | select(.name == "resident")')
api POST prompts/resident/check "$(jq -c '{text: .text}' <<< "$resident")" > /dev/null
api PUT prompts/resident "$(jq -c '{hash: .hash, text: .text}' <<< "$resident")" > /dev/null
echo "management API: ok"

rpc=0
mcp() {
    local method=$1 params=$2 request answer data
    rpc=$((rpc + 1))
    request=$(jq -cn --arg method "$method" --argjson id "$rpc" --argjson params "$params" \
        '{jsonrpc: "2.0", id: $id, method: $method, params: $params}')
    answer=$(curl -sS -X POST "$base/mcp" -H 'Content-Type: application/json' \
        -H 'Accept: application/json, text/event-stream' -H 'MCP-Protocol-Version: 2025-11-25' --data "$request") ||
        fail "$method could not be sent"
    # The answer is one server-sent event; its data line is the JSON-RPC response.
    data=$(printf '%s\n' "$answer" | tr -d '\r' | grep -m 1 '^data:' | cut -c 6-) || fail "$method returned no event"
    if [ "$(jq -r 'has("error")' <<< "$data" | tr -d '\r')" = true ]; then
        fail "$method failed: $(jq_value '.error.message' <<< "$data")"
    fi
    jq -c '.result' <<< "$data"
}
tool() {
    local name=$1 arguments=$2 result text
    result=$(mcp tools/call "$(jq -cn --arg name "$name" --argjson arguments "$arguments" '{name: $name, arguments: $arguments}')")
    text=$(jq_value '[.content[].text] | join("\n")' <<< "$result")
    if [ "$(jq_value '.isError // false' <<< "$result")" = true ]; then fail "$name refused: $text"; fi
    printf '%s\n' "$text"
}

tools=$(mcp tools/list '{}' | jq_value '[.tools[].name] | sort | join(",")')
[ "$tools" = recall,write_page,write_persona,write_recent ] || fail "tools/list returned '$tools'"
tool write_page '{"op": "create", "page": "smoke/check", "title": "Smoke check", "type": "work",
    "aliases": ["smoke page", "release check"],
    "sections": [{"name": "Output", "body": "The release package answers every kind of request."}]}' > /dev/null
tool write_recent '{"text": "The smoke check wrote a page.", "workstream": "smoke"}' > /dev/null
read=$(tool recall '{"page": "smoke/check"}')
case $read in
    *"every kind of request"*) ;;
    *) fail "recall did not return the page it wrote: $read" ;;
esac
tool recall '{"query": "release package"}' > /dev/null
echo "MCP tools: $tools"

api POST reindex '{}' > /dev/null
location=$(api POST backup '{"target": "local"}' | jq_value '.location')
preview=$(api POST restore "$(jq -cn --arg path "$location" '{path: $path, preview: true}')")
[ -n "$(jq_value '.memoryHead // empty' <<< "$preview")" ] || fail "the restore preview of the new backup names no commit"
echo "index, backup and restore preview: ok"

"$tacit" status > /dev/null || fail "tacit status exited with $?"
echo "tacit status: ok"

page=$(curl -sS -w '\n%{http_code}' "$base/ui/")
[ "${page##*$'\n'}" = 200 ] || fail "/ui/ answered ${page##*$'\n'}"
case $page in
    *"<html"*) ;;
    *) fail "/ui/ did not return the web interface" ;;
esac
echo "web interface: ok"

passed=true
echo "Smoke checks passed."
