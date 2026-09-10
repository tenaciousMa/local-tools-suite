param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipAppPublish
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "installer\build-installer.ps1") `
    -Version $Version `
    -Configuration $Configuration `
    -SkipAppPublish:$SkipAppPublish
