param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipAppPublish
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "desktop\video\VideoGridDesktop.csproj"
$appDir = Join-Path $root "dist\视频截图拼图台"
$appExe = Join-Path $appDir "视频截图拼图台.exe"
$isccPath = Join-Path $PSScriptRoot "VideoGridSetup.iss"

if (-not $SkipAppPublish) {
    $running = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($appDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        $ids = ($running | Select-Object -ExpandProperty Id) -join ", "
        throw "应用正在运行，无法覆盖发布目录。请先关闭进程：$ids"
    }

    dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -o $appDir `
        --disable-build-servers

    Copy-Item -LiteralPath (Join-Path $appDir "VideoGridDesktop.exe") -Destination $appExe -Force
    Remove-Item -LiteralPath (Join-Path $appDir "VideoGridDesktop.exe") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $appDir "VideoGridDesktop.pdb") -Force -ErrorAction SilentlyContinue
}

$compilerCandidates = @(
    $env:ISCC,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$compiler = $null
foreach ($candidate in $compilerCandidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
        $compiler = $candidate
        break
    }
}
if (-not $compiler) {
    Write-Host "未找到 Inno Setup，正在为当前用户自动安装..."
    & (Join-Path $PSScriptRoot "bootstrap-inno.ps1")
    $compiler = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
}

if (-not $compiler -or -not (Test-Path -LiteralPath $compiler)) {
    throw "未找到 Inno Setup 编译器 ISCC.exe。"
}

& $compiler "/DAppVersion=$Version" $isccPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出代码：$LASTEXITCODE"
}

$setup = Join-Path $root "dist\installer\视频截图拼图台_安装包_v$Version.exe"
Write-Host "安装包已生成：$setup"
