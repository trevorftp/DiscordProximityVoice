param(
    [string]$SdkZip,
    [string]$SdkRoot,
    [switch]$OpenDownloads,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$NativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ModRoot = Split-Path -Parent $NativeRoot
$BuildRoot = Join-Path $NativeRoot 'build'
$ExtractRoot = Join-Path $NativeRoot '_sdk'

function Resolve-SdkRoot {
    param([string]$Root)

    if ([string]::IsNullOrWhiteSpace($Root)) { return $null }

    $resolved = Resolve-Path -LiteralPath $Root -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Path
    if (Test-Path -LiteralPath (Join-Path $resolved 'include\discordpp.h')) { return $resolved }

    $nested = Get-ChildItem -LiteralPath $resolved -Directory -Recurse -Filter 'discord_social_sdk' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $nested -and (Test-Path -LiteralPath (Join-Path $nested.FullName 'include\discordpp.h'))) { return $nested.FullName }

    throw "Could not find include\discordpp.h under $resolved"
}

if ($OpenDownloads) {
    Start-Process 'https://discord.com/developers/applications/select/social-sdk/downloads'
}

if (-not [string]::IsNullOrWhiteSpace($SdkZip)) {
    if (Test-Path -LiteralPath $ExtractRoot) { Remove-Item -LiteralPath $ExtractRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ExtractRoot | Out-Null
    Expand-Archive -LiteralPath $SdkZip -DestinationPath $ExtractRoot -Force
    $SdkRoot = $ExtractRoot
}

$ResolvedSdkRoot = Resolve-SdkRoot $SdkRoot
if ($null -eq $ResolvedSdkRoot) {
    throw "Pass -SdkZip <downloaded zip> or -SdkRoot <extracted discord_social_sdk>. Use -OpenDownloads to open the gated Discord download page."
}

$RuntimeDll = Join-Path $ResolvedSdkRoot 'bin\release\discord_partner_sdk.dll'
$ImportLib = Join-Path $ResolvedSdkRoot 'lib\release\discord_partner_sdk.lib'

if (-not (Test-Path -LiteralPath $RuntimeDll)) { throw "Missing $RuntimeDll" }
if (-not (Test-Path -LiteralPath $ImportLib)) { throw "Missing $ImportLib" }

Copy-Item -LiteralPath $RuntimeDll -Destination (Join-Path $NativeRoot 'discord_partner_sdk.dll') -Force
Write-Host "Copied discord_partner_sdk.dll"

if (-not $SkipBuild) {
    if ($null -eq (Get-Command cmake -ErrorAction SilentlyContinue)) {
        throw "cmake is not available on PATH. Install CMake or run this from a Visual Studio developer shell with CMake available."
    }

    cmake -S $NativeRoot -B $BuildRoot -A x64 -DDISCORD_SDK_ROOT="$ResolvedSdkRoot"
    cmake --build $BuildRoot --config Release

    $BridgeDll = Get-ChildItem -LiteralPath $BuildRoot -Recurse -Filter 'DiscordProximityVoice.Native.dll' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $BridgeDll) { throw "Native bridge build completed, but DiscordProximityVoice.Native.dll was not found under $BuildRoot" }

    Copy-Item -LiteralPath $BridgeDll.FullName -Destination (Join-Path $NativeRoot 'DiscordProximityVoice.Native.dll') -Force
    Write-Host "Copied DiscordProximityVoice.Native.dll"
}

Write-Host "Native files are staged in $NativeRoot"
Write-Host "Next: set DiscordApplicationId in the server ModConfig\DiscordProximityVoice.Server.json, then build the mod."