param(
    [string]$Owner = "tenaciousMa",
    [string]$Repository = "local-tools-suite",
    [string]$ReleaseTag = "v1.0.0",
    [string]$InstallerAssetName = "VideoGridTool-Setup.exe",
    [string]$RenamerAssetName = "FileRenamerTool.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$installerPath = Join-Path $root "dist\installer\视频截图拼图台_安装包_v1.0.0.exe"
$renamerPath = Join-Path $root "desktop\publish\FileRenamerDesktop.exe"
$token = $env:GITHUB_TOKEN

if ([string]::IsNullOrWhiteSpace($token)) {
    $secureToken = Read-Host "请输入 GitHub Token" -AsSecureString
    $token = [System.Net.NetworkCredential]::new("", $secureToken).Password
}

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "GitHub Token 不能为空。"
}

$headers = @{
    Authorization = "Bearer $token"
    Accept = "application/vnd.github+json"
    "User-Agent" = "VideoGridStudioPublisher"
    "X-GitHub-Api-Version" = "2022-11-28"
}

function Invoke-GitHubApi {
    param(
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        Headers = $headers
    }
    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }

    return Invoke-RestMethod @parameters
}

Write-Host "验证 GitHub 账号..."
$user = Invoke-GitHubApi -Method "Get" -Uri "https://api.github.com/user"
if ($user.login -ne $Owner) {
    throw "Token 所属账号为 $($user.login)，与目标账号 $Owner 不一致。"
}

Write-Host "检查仓库 $Owner/$Repository..."
try {
    $repositoryInfo = Invoke-GitHubApi -Method "Get" -Uri "https://api.github.com/repos/$Owner/$Repository"
} catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) {
        throw
    }

    $repositoryInfo = Invoke-GitHubApi -Method "Post" -Uri "https://api.github.com/user/repos" -Body @{
        name = $Repository
        private = $false
        description = "Windows 本地工具套件：文件改名、视频选帧、拼图排版和 PDF 导出"
        has_issues = $true
        has_projects = $false
        has_wiki = $false
        auto_init = $false
    }
}

$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
git -C $root remote remove origin 2>$null | Out-Null
$ErrorActionPreference = $previousErrorAction
git -C $root remote add origin "https://github.com/$Owner/$Repository.git"

git -C $root add .
$pending = git -C $root status --porcelain
if ($pending) {
    git -C $root commit -m "Publish static showcase and release v1.0.0"
}

Write-Host "推送 main 分支..."
$askPassPath = Join-Path $env:TEMP "video-grid-git-askpass.cmd"
@"
@echo off
echo %VIDEO_GRID_GIT_TOKEN%
"@ | Set-Content -LiteralPath $askPassPath -Encoding ASCII

try {
    $env:VIDEO_GRID_GIT_TOKEN = $token
    $env:GIT_ASKPASS = $askPassPath
    $env:GIT_TERMINAL_PROMPT = "0"
    git -C $root -c credential.helper= -c core.askpass="$askPassPath" push -u origin main
    if ($LASTEXITCODE -ne 0) {
        throw "git push 失败，退出代码：$LASTEXITCODE"
    }
} finally {
    Remove-Item Env:VIDEO_GRID_GIT_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:GIT_ASKPASS -ErrorAction SilentlyContinue
    Remove-Item Env:GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $askPassPath -Force -ErrorAction SilentlyContinue
}

Write-Host "创建或更新 Release..."
$release = $null
try {
    $release = Invoke-GitHubApi -Method "Get" -Uri "https://api.github.com/repos/$Owner/$Repository/releases/tags/$ReleaseTag"
} catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) {
        throw
    }

    $release = Invoke-GitHubApi -Method "Post" -Uri "https://api.github.com/repos/$Owner/$Repository/releases" -Body @{
        tag_name = $ReleaseTag
        target_commitish = "main"
        name = "视频截图拼图台 v1.0.0"
        body = "包含文件改名台绿色版和视频截图拼图台 Windows 安装包。安装包支持自定义目录、桌面和开始菜单快捷方式以及标准卸载。"
        draft = $false
        prerelease = $false
    }
}

function Upload-ReleaseAsset {
    param(
        [string]$Path,
        [string]$AssetName
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "发布文件不存在：$Path"
    }

    foreach ($asset in $release.assets) {
        if ($asset.name -eq $AssetName) {
            Invoke-GitHubApi -Method "Delete" -Uri $asset.url | Out-Null
            break
        }
    }

    $client = [System.Net.Http.HttpClient]::new()
    $client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoGridStudioPublisher")

    $stream = [System.IO.File]::OpenRead($Path)
    $content = [System.Net.Http.StreamContent]::new($stream)
    $content.Headers.ContentType =
        [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/octet-stream")

    try {
        $uploadUri = $release.upload_url.Replace("{?name,label}", "?name=$([Uri]::EscapeDataString($AssetName))")
        $response = $client.PostAsync($uploadUri, $content).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            $detail = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            throw "上传 $AssetName 失败：$detail"
        }
    } finally {
        $content.Dispose()
        $stream.Dispose()
        $client.Dispose()
    }
}

Write-Host "上传 Release 文件..."
Upload-ReleaseAsset -Path $installerPath -AssetName $InstallerAssetName
if (Test-Path -LiteralPath $renamerPath) {
    Upload-ReleaseAsset -Path $renamerPath -AssetName $RenamerAssetName
}

Write-Host "启用 GitHub Pages..."
try {
    Invoke-GitHubApi -Method "Get" -Uri "https://api.github.com/repos/$Owner/$Repository/pages" |
        Out-Null
    Invoke-GitHubApi -Method "Put" -Uri "https://api.github.com/repos/$Owner/$Repository/pages" -Body @{
        source = @{
            branch = "main"
            path = "/docs"
        }
    } | Out-Null
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        Invoke-GitHubApi -Method "Post" -Uri "https://api.github.com/repos/$Owner/$Repository/pages" -Body @{
            source = @{
                branch = "main"
                path = "/docs"
            }
        } | Out-Null
    } else {
        Write-Warning "Pages 自动设置失败，请检查仓库 Settings > Pages。$($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "GitHub 发布完成："
Write-Host "仓库：https://github.com/$Owner/$Repository"
Write-Host "下载：https://github.com/$Owner/$Repository/releases/latest"
Write-Host "网页：https://$Owner.github.io/$Repository/"
