#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_path="$(cd -- "$script_dir/../../.." && pwd)"

dotnet_path=""
if command -v dotnet >/dev/null 2>&1; then
  dotnet_path="$(command -v dotnet)"
else
  for candidate in \
    /usr/local/share/dotnet/dotnet \
    /opt/homebrew/bin/dotnet \
    /usr/local/bin/dotnet; do
    if [[ -x "$candidate" ]]; then
      dotnet_path="$candidate"
      break
    fi
  done
fi

if [[ -z "$dotnet_path" ]]; then
  echo "Ruleforge CLI requires the .NET SDK, but dotnet was not found." >&2
  echo "Install .NET 8 or newer, then run this script again." >&2
  exit 127
fi

exec "$dotnet_path" run \
  --configuration Release \
  --project "$repo_path/Tools/Ruleforge.BalanceCli" \
  -- \
  watch \
  --repo "$repo_path" \
  "$@"
