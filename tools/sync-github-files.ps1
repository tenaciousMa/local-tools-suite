param(
    [string]$Owner = "tenaciousMa",
    [string]$Repository = "local-tools-suite",
    [string]$Branch = "main",
    [string[]]$Files
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$token = $env:GITHUB_TOKEN

if ([string]::IsNullOrWhiteSpace($token)) {
    $secureToken = Read-Host "请输入 GitHub Token" -AsSecureString
    $token = [System.Net.NetworkCredential]::new("", $secureToken).Password
}

$headers = @{
    Authorization = "Bearer $token"
    Accept = "application/vnd.github+json"
    "User-Agent" = "VideoGridStudioPublisher"
    "X-GitHub-Api-Version" = "2022-11-28"
}

if (-not $Files -or $Files.Count -eq 0) {
    $Files = @(
        "docs/app.js",
        "docs/assets/renamer-main.png",
        "docs/index.html",
        "docs/renamer/index.html",
        "docs/styles.css",
        "docs/video-grid/index.html",
        "tools/publish-github.ps1",
        "tools/sync-github-files.ps1",
        "tools/verify-site.mjs"
    )
}

foreach ($relativePath in $Files) {
    $normalized = $relativePath.Replace("\", "/")
    $fullPath = Join-Path $root $normalized
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "文件不存在：$fullPath"
    }

    $encodedPath = ($normalized -split "/" | ForEach-Object {
        [Uri]::EscapeDataString($_)
    }) -join "/"
    $contentsUri = "https://api.github.com/repos/$Owner/$Repository/contents/$encodedPath"
    $sha = $null

    try {
        $existing = Invoke-RestMethod `
            -Method "Get" `
            -Uri "${contentsUri}?ref=$([Uri]::EscapeDataString($Branch))" `
            -Headers $headers
        $sha = $existing.sha
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) {
            throw
        }
    }

    $content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($fullPath))
    $body = @{
        message = "Publish static showcase pages"
        content = $content
        branch = $Branch
    }
    if ($sha) {
        $body.sha = $sha
    }

    Invoke-RestMethod `
        -Method "Put" `
        -Uri $contentsUri `
        -Headers $headers `
        -ContentType "application/json" `
        -Body ($body | ConvertTo-Json -Depth 5 -Compress) | Out-Null

    Write-Host "已同步：$normalized"
}

Write-Host "GitHub 文件同步完成。"
