param(
    [string]$ConfigRoot = $PSScriptRoot,
    [string]$LubanDll = $env:LUBAN_DLL,
    [string]$Dotnet = $env:DOTNET_EXE,
    [string]$CodeOutput,
    [string]$DataOutput,
    [string]$JsonOutput,
    [switch]$Eager
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'ConfigWorkbook.ps1')
$ConfigRoot = (Resolve-Path -LiteralPath $ConfigRoot).Path
$projectRoot = Split-Path $ConfigRoot -Parent
if (!(Test-Path -LiteralPath (Join-Path $ConfigRoot 'luban.conf'))) { throw 'ConfigRoot must contain luban.conf.' }

if (!$LubanDll) {
    $candidates = @()
    $toolRoot = $projectRoot
    for ($depth = 0; $depth -le 2 -and $toolRoot; $depth++) {
        $candidates += Join-Path $toolRoot 'Tools/Luban/Luban.dll'
        $toolRoot = Split-Path $toolRoot -Parent
    }
    # Existing local installation used by this checkout; environment override takes precedence.
    if ($env:OS -eq 'Windows_NT') { $candidates += 'D:\Luban\Luban\Luban.dll' }
    $LubanDll = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$LubanDll -or !(Test-Path -LiteralPath $LubanDll -PathType Leaf)) { throw 'Luban.dll not found. Set LUBAN_DLL or pass -LubanDll; see GameConfig/README.md.' }
if (!$Dotnet) {
    $runtime = Join-Path (Split-Path (Split-Path $LubanDll -Parent) -Parent) 'dotnet/dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $runtime) { $runtime } else { 'dotnet' }
}
if (!$CodeOutput -or !$DataOutput) {
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot 'Assets') -PathType Container)) { throw 'Place GameConfig next to Assets, or provide both output directories.' }
    if (!$CodeOutput) { $CodeOutput = Join-Path $projectRoot 'Assets/GameScripts/HotFix/GameProto/GameConfig' }
    if (!$DataOutput) { $DataOutput = Join-Path $projectRoot 'Assets/AssetRaw/Configs/bytes' }
}
$CodeOutput = [IO.Path]::GetFullPath($CodeOutput)
$DataOutput = [IO.Path]::GetFullPath($DataOutput)
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ('FreshGuardConfig_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $inputRoot = Join-Path $temporaryRoot 'input'
    New-Item -ItemType Directory -Path $inputRoot | Out-Null
    foreach ($name in @('Datas', 'Defines', 'CustomTemplate')) {
        $source = Join-Path $ConfigRoot $name
        $destination = Join-Path $inputRoot $name
        New-Item -ItemType Directory -Path $destination | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            if ($file.Name.StartsWith('~$')) { continue }
            $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]'\/')
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-SharedFile $file.FullName $target
        }
    }
    Copy-SharedFile (Join-Path $ConfigRoot 'luban.conf') (Join-Path $inputRoot 'luban.conf')
    Prepare-ConfigWorkbooks (Join-Path $inputRoot 'Datas')

    $code = Join-Path $temporaryRoot 'code'
    $data = Join-Path $temporaryRoot 'bytes'
    $arguments = @('-t', 'client', '-c', 'cs-bin', '-d', 'bin', '--conf', (Join-Path $inputRoot 'luban.conf'),
        '-x', 'code.lineEnding=crlf', '-x', ('outputCodeDir=' + $code), '-x', ('outputDataDir=' + $data))
    if (!$Eager) { $arguments += @('--customTemplateDir', (Join-Path $inputRoot 'CustomTemplate/CustomTemplate_Client_LazyLoad')) }
    & $Dotnet $LubanDll @arguments
    if ($LASTEXITCODE -ne 0) { throw "Luban export failed ($LASTEXITCODE); project outputs were not updated." }
    if ($JsonOutput) {
        & $Dotnet $LubanDll -t client -d json --conf (Join-Path $inputRoot 'luban.conf') -x ('outputDataDir=' + (Join-Path $temporaryRoot 'json'))
        if ($LASTEXITCODE -ne 0) { throw 'Luban JSON validation export failed; project outputs were not updated.' }
    }
    foreach ($pair in @(@($code, $CodeOutput), @($data, $DataOutput))) {
        foreach ($file in Get-ChildItem -LiteralPath $pair[0] -Recurse -File) {
            $relative = $file.FullName.Substring($pair[0].Length).TrimStart([char[]]'\/')
            $target = Join-Path $pair[1] $relative
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    if ($JsonOutput) {
        New-Item -ItemType Directory -Path $JsonOutput -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $temporaryRoot 'json') -File | Copy-Item -Destination $JsonOutput -Force
    }
    $protoRoot = Split-Path $CodeOutput -Parent
    foreach ($name in @('ConfigSystem.cs', 'ExternalTypeUtil.cs')) {
        Copy-Item -LiteralPath (Join-Path $inputRoot ('CustomTemplate/' + $name)) -Destination (Join-Path $protoRoot $name) -Force
    }
    Write-Output '[Luban] Export succeeded. Simplified source workbooks were not modified.'
} finally {
    # Delete only the task-created direct child of the operating system's temporary directory.
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
    if ((Split-Path $resolvedTemporary -Parent).TrimEnd([char[]]'\/') -ne $temporaryParent.TrimEnd([char[]]'\/') -or
        (Split-Path $resolvedTemporary -Leaf) -notmatch '^FreshGuardConfig_[0-9a-f]{32}$') { throw 'Unsafe temporary cleanup path.' }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
