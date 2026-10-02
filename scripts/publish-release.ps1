# 把 dist\ 里那三颗挂成一个 Release（批次 UH）。
# 用法：powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1 -Tag v1.0.0
#
# 顺序是刻意排的：**先草稿、后公开**，而且公开这一步只在"从 API 读回来对过账"之后才做。
# 为什么：这一版一旦公开，所有装着的程序下一发检查就会看见它并真去下载替换——
# 那一步没有"撤回已经装上去的文件"这种操作，所以它只能在证据齐了之后发生。
# 读回来的判据是名字与字节数（三颗一个都不能少、一个都不能错）：
# 少 zip ⇒ 用户点「立即更新」只得到"这一版没带更新包"；少清单或签名 ⇒ 探测报有新版而审包必拒。
#
# 凭据：读应用自己存的那把（%APPDATA%\StarMark\github.json 的 Token），**它一路只进 header，不进输出、不入库**。
# 脚本里不许出现任何把 Token 打出来的语句（有闸门钉着这条）。
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Repository = "AH23333/StarMarkDesktop",   # 必须与 UpdatePolicy.DefaultRepository 同一颗（有闸门钉着）
    [string]$DistDir
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $DistDir) { $DistDir = Join-Path $root "dist" }

$version = $Tag -replace '^v', ''
foreach ($name in @("StarMark-$version-win-x64.zip", "update-manifest.json", "update-manifest.sig")) {
    if (-not (Test-Path (Join-Path $DistDir $name))) { throw "少一颗：$name（先跑 scripts\release-package.ps1）" }
}

$cfgPath = Join-Path $env:APPDATA "StarMark\github.json"
if (-not (Test-Path $cfgPath)) { throw "读不到应用的凭据：$cfgPath" }
$token = (Get-Content -Raw $cfgPath | ConvertFrom-Json).Token
if ([string]::IsNullOrWhiteSpace($token)) { throw "应用存的那把 Token 是空的" }
$headers = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "X-GitHub-Api-Version" = "2022-11-28" }
$api = "https://api.github.com/repos/$Repository"

Write-Host "==> 1/4 建草稿（标签 $Tag）" -ForegroundColor Cyan
$body = @{ tag_name = $Tag; name = "StarMark $Tag"; body = "桌面版 $version：框架依赖，需 .NET 9 Desktop Runtime。"; draft = $true; prerelease = $false } | ConvertTo-Json
try {
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType "application/json" -Body $body
} catch {
    # 三种失败说的话不一样，混成一句就会指错方向（这一版真踩到过一次：403 权限问题被报成了"标签已存在"）：
    #   401/403 ⇒ 这把凭据没有建仓 Release 的权限，要去 GitHub 上调，不是重跑一次能好的；
    #   422     ⇒ 那个标签已经有 Release 了，覆盖它会改变别人机器上已经装到的东西，必须人来确认；
    #   其它    ⇒ 照实说，并把 API 的原话带上。
    $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    $detail = $_.ErrorDetails.Message
    if ($code -eq 401 -or $code -eq 403) {
        throw "这把 Token 没有建仓 Release 的权限（HTTP $code）：去 GitHub 把细粒度令牌的 Contents 改成 Read and write（只给这一个仓库就够）。API 原话：$detail"
    }
    throw "建草稿失败（HTTP $code）：$detail —— 若那个标签已经有 Release，别覆盖它：那会改变别人机器上已经装到的一版"
}

Write-Host "==> 2/4 上传三颗资产" -ForegroundColor Cyan
$uploads = @(
    @{ Name = "StarMark-$version-win-x64.zip"; Mime = "application/zip" },
    @{ Name = "update-manifest.json";          Mime = "application/json" },
    @{ Name = "update-manifest.sig";           Mime = "application/octet-stream" })
foreach ($u in $uploads) {
    $path = Join-Path $DistDir $u.Name
    Invoke-RestMethod -Method Post -Uri "$($release.upload_url)?name=$($u.Name)" `
        -Headers $headers -ContentType $u.Mime -InFile $path | Out-Null
    Write-Host ("  已传 {0}" -f $u.Name)
}

Write-Host "==> 3/4 从 API 读回来对账（名字与字节数，一颗都不许差）" -ForegroundColor Cyan
$fresh = Invoke-RestMethod -Method Get -Uri "$api/releases/$($release.id)" -Headers $headers
$byName = @{}
foreach ($a in $fresh.assets) { $byName[$a.name] = [int64]$a.size }
foreach ($u in $uploads) {
    $local = (Get-Item (Join-Path $DistDir $u.Name)).Length
    if (-not $byName.ContainsKey($u.Name)) { throw "远端没有这颗：$($u.Name)（草稿留着，别当发出去了）" }
    if ($byName[$u.Name] -ne $local) { throw "这颗字节数对不上：$($u.Name) 本地 $local / 远端 $($byName[$u.Name])" }
}
Write-Host ("  三颗已在，字节数与本地一致（载荷 {0:N0} 字节）" -f $byName["StarMark-$version-win-x64.zip"])

Write-Host "==> 4/4 证据齐了才公开" -ForegroundColor Cyan
Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType "application/json" `
    -Body (@{ draft = $false } | ConvertTo-Json) | Out-Null
Write-Host ""
Write-Host "已发布：$($fresh.html_url)"
Write-Host "装着的程序下一发检查就会看见 $Tag；界面上那颗「立即更新」从此有东西可下。"
