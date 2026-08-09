#!/usr/bin/env bash

set -uo pipefail

repo_path="${1:-$(cd "$(dirname "$0")/../../.." && pwd)}"
output_root="${2:-$repo_path/Artifacts/Balance/background-100/$(date +%Y%m%d-%H%M%S)}"
dotnet_bin="${DOTNET_BIN:-/Users/lim/.dotnet/dotnet}"
cli_dll="$repo_path/Tools/Ruleforge.BalanceCli/bin/Release/net8.0/Ruleforge.BalanceCli.dll"

mkdir -p "$output_root/logs"

write_status() {
    status_name="$1"
    finished_at="${2:-}"
    printf '%s\n' \
        '{' \
        "  \"status\": \"$status_name\"," \
        "  \"startedAt\": \"$started_at\"," \
        "  \"finishedAt\": \"$finished_at\"," \
        "  \"outputRoot\": \"$output_root\"," \
        '  "plannedRuns": {' \
        '    "easy": 102,' \
        '    "medium": 100,' \
        '    "hard": 102,' \
        '    "total": 304' \
        '  }' \
        '}' > "$output_root/status.json"
}

run_batch() {
    difficulty="$1"
    policy="$2"
    limit="$3"
    shift 3
    batch_dir="$output_root/$difficulty/$policy"
    log_file="$output_root/logs/$difficulty-$policy.log"

    mkdir -p "$batch_dir"
    "$dotnet_bin" "$cli_dll" batch \
        --repo "$repo_path" \
        --difficulty "$difficulty" \
        --policy "$policy" \
        --seed-set train \
        --limit "$limit" \
        --output-dir "$batch_dir" \
        "$@" > "$log_file" 2>&1
    exit_code=$?
    printf '%s\n' "$exit_code" > "$batch_dir/exit-code.txt"
    return 0
}

if [[ ! -x "$dotnet_bin" ]]; then
    printf 'dotnet executable not found: %s\n' "$dotnet_bin" >&2
    exit 2
fi

if [[ ! -f "$cli_dll" ]]; then
    printf 'Release CLI not found: %s\n' "$cli_dll" >&2
    exit 2
fi

started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
write_status "running"

(
    run_batch easy novice-ensemble 34 \
        --card-strength "$repo_path/Artifacts/Balance/final/indices/easy/card-strength-index.json"
) &
easy_pid=$!

(
    run_batch medium good-standalone 50 \
        --card-strength "$repo_path/Artifacts/Balance/final/indices/medium/card-strength-index.json"
    run_batch medium synergy-tactical 50 \
        --card-strength "$repo_path/Artifacts/Balance/final/indices/medium/card-strength-index.json"
) &
medium_pid=$!

(
    run_batch hard synergy-tactical 34 \
        --card-strength "$repo_path/Artifacts/Balance/final/indices/hard/card-strength-index.json" \
        --card-synergy "$repo_path/Artifacts/Balance/final/indices/hard/triple-beam/card-synergy-index.json"
    run_batch hard synergy-no-combat-build 34 \
        --card-strength "$repo_path/Artifacts/Balance/final/indices/hard/card-strength-index.json" \
        --card-synergy "$repo_path/Artifacts/Balance/final/indices/hard/triple-beam/card-synergy-index.json"
    run_batch hard synergy-disabled 34 \
        --card-strength "$repo_path/Artifacts/Balance/final/indices/hard/card-strength-index.json"
) &
hard_pid=$!

wait "$easy_pid"
wait "$medium_pid"
wait "$hard_pid"

finished_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
write_status "complete" "$finished_at"
touch "$output_root/COMPLETE"
