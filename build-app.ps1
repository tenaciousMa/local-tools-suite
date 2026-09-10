param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $PSScriptRoot).Path
$appDir = Join-Path $root "dist\视频截图拼图台"
$appExe = Join-Path $appDir "视频截图拼图台.exe"

dotnet publish (Join-Path $root "desktop\video\VideoGridDesktop.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -o $appDir `
    --disable-build-servers

Copy-Item -LiteralPath (Join-Path $appDir "VideoGridDesktop.exe") -Destination $appExe -Force
Remove-Item -LiteralPath (Join-Path $appDir "VideoGridDesktop.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $appDir "VideoGridDesktop.pdb") -Force -ErrorAction SilentlyContinue

Write-Host "应用发布完成：$appExe"
