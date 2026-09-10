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

function Invoke-WithRetry {
    param(
        [scriptblock]$Action,
        [int]$Attempts = 3
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            return & $Action
        } catch {
            if ($attempt -eq $Attempts) {
                throw
            }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

if (-not $Files -or $Files.Count -eq 0) {
    $Files = @(
        "README.md",
        "docs/app.js",
        "docs/assets/contact-sheet-bunny.png",
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
        $existing = Invoke-WithRetry {
            Invoke-RestMethod `
                -Method "Get" `
                -Uri "${contentsUri}?ref=$([Uri]::EscapeDataString($Branch))" `
                -Headers $headers
        }
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

    Invoke-WithRetry {
        Invoke-RestMethod `
            -Method "Put" `
            -Uri $contentsUri `
            -Headers $headers `
            -ContentType "application/json" `
            -Body ($body | ConvertTo-Json -Depth 5 -Compress) | Out-Null
    } | Out-Null

    Write-Host "已同步：$normalized"
}

try {
    $siteUrl = "https://htmlpreview.github.io/?https://raw.githubusercontent.com/$Owner/$Repository/main/docs/index.html"
    Invoke-WithRetry {
        Invoke-RestMethod `
            -Method "Patch" `
            -Uri "https://api.github.com/repos/$Owner/$Repository" `
            -Headers $headers `
            -ContentType "application/json" `
            -Body (@{
                homepage = $siteUrl
                description = "Windows 本地工具套件：文件批量改名、视频截图拼图、多页排版和 PDF 导出"
            } | ConvertTo-Json -Compress) | Out-Null
    } | Out-Null
    Write-Host "已更新仓库主页链接。"
} catch {
    Write-Warning "仓库主页链接更新失败：$($_.Exception.Message)"
}

Write-Host "GitHub 文件同步完成。"
