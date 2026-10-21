#!/usr/bin/env bash
set -euo pipefail
export DOTNET_ROOT=/opt/waddamburo-dotnet
export PATH="$DOTNET_ROOT:$PATH"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
dotnet publish "$root/tests/Waddamburo.Prospero.RealmChecks" -c Release -r linux-x64 -o "$root/out/build/realm-host-checks"
"$root/out/build/realm-host-checks/RealmChecks"
