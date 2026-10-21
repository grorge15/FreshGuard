#!/bin/bash

cd "$(dirname "$0")"
echo "当前目录: $(pwd)"

export WORKSPACE="$(realpath ../../)"
export PROJECT_ROOT="$(cd .. && pwd)"
export LUBAN_DLL="${WORKSPACE}/Tools/Luban/Luban.dll"
export CONF_ROOT="$(pwd)"
export DATA_OUTPATH="${PROJECT_ROOT}/Assets/AssetRaw/Configs/bytes/"
export CODE_OUTPATH="${PROJECT_ROOT}/Assets/GameScripts/HotFix/GameProto/GameConfig/"

if [ ! -d "${PROJECT_ROOT}/Assets" ]; then
    echo "[ERROR] Move this GameConfig folder to UnityProject/GameConfig before exporting." >&2
    exit 1
fi

cp -R "${CONF_ROOT}/CustomTemplate/ConfigSystem.cs" \
   "${PROJECT_ROOT}/Assets/GameScripts/HotFix/GameProto/ConfigSystem.cs"
cp -R "${CONF_ROOT}/CustomTemplate/ExternalTypeUtil.cs" \
    "${PROJECT_ROOT}/Assets/GameScripts/HotFix/GameProto/ExternalTypeUtil.cs"

dotnet "${LUBAN_DLL}" \
    -t client \
    -c cs-bin \
    -d bin \
    --conf "${CONF_ROOT}/luban.conf" \
    --customTemplateDir "${CONF_ROOT}/CustomTemplate/CustomTemplate_Client_LazyLoad" \
    -x code.lineEnding=crlf \
    -x outputCodeDir="${CODE_OUTPATH}" \
    -x outputDataDir="${DATA_OUTPATH}"
