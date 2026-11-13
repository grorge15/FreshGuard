#!/bin/bash
set -euo pipefail
if ! command -v pwsh >/dev/null 2>&1; then
    echo "[ERROR] PowerShell 7 (pwsh) is required to prepare simplified workbooks. See GameConfig/README.md." >&2
    exit 1
fi
exec pwsh -NoProfile -File "$(cd "$(dirname "$0")" && pwd)/Export-Configs.ps1"
