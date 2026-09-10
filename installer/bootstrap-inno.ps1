param(
    [string]$Version = "6.7.3"
)

$ErrorActionPreference = "Stop"
$downloadUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-$Version.exe"
$downloadPath = Join-Path $env:TEMP "innosetup-$Version.exe"

if (-not (Test-Path -LiteralPath $downloadPath)) {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -UseBasicParsing
}

$process = Start-Process `
    -FilePath $downloadPath `
    -ArgumentList '/VERYSILENT', '/CURRENTUSER', '/NORESTART', '/SP-', '/SUPPRESSMSGBOXES' `
    -PassThru `
    -Wait `
    -WindowStyle Hidden

if ($process.ExitCode -ne 0) {
    throw "Inno Setup 安装失败，退出代码：$($process.ExitCode)"
}

$compiler = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Inno Setup 安装完成后仍未找到 ISCC.exe。"
}

Write-Host "Inno Setup 已安装：$compiler"
