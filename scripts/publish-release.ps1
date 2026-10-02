# 把 dist\ 里那三颗挂成一个 Release（批次 UH）。
# 用法：powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1 -Tag v1.0.0
#       那一版已经公开过、要换载荷：再加 -ReplaceAssets（不给这一句就直接拒绝，理由见下）
#
# 顺序是刻意排的：**先问那个标签上挂着什么 → 草稿（或沿用现有那条）→ 三颗就位 → 从 API 读回来对账 → 才公开**。
# 为什么：这一版一旦公开，所有装着的程序下一发检查就会看见它并真去下载替换——
# 那一步没有"撤回已经装上去的文件"这种操作，所以它只能在证据齐了之后发生。
# 读回来的判据是名字与字节数（三颗一个都不能少、一个都不能错）：
# 少 zip ⇒ 用户点「立即更新」只得到"这一版没带更新包"；少清单或签名 ⇒ 探测报有新版而审包必拒。
#
# 换载荷为什么必须三颗一起换：清单里的哈希对着 zip，签名又对着清单那串字节。只换 zip 留下的是
# "新包配旧清单"，用户那一侧的表现是审包永远拒收，而这一版在界面上一直显示"有更新"却装不上。
# 也不要按"字节数相同就跳过"来省一次上传：重新签一次名出来的串**和原来一样长**，
# 跳过就等于把旧签名留在上面——字节数能证"传丢了没有"，证不了"内容变了没有"。
#
# 删与传之间有一个"资产不全"的窗口，这是可以忍的：那三颗是审包链的三节，缺任何一节的后果都是
# **拒收而不是装错**（少 zip 报"这一版没带更新包"，少清单或签名根本读不成），下一发检查再试就过去了。
# 反过来说，一份三节对不齐的载荷发出去才是修不好的。
#
# 凭据：读应用自己存的那把（%APPDATA%\StarMark\github.json 的 Token），**它一路只进 header，不进输出、不入库**。
# 脚本里不许出现任何把 Token 打出来的语句（有闸门钉着这条）。
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Repository = "AH23333/StarMarkDesktop",   # 必须与 UpdatePolicy.DefaultRepository 同一颗（有闸门钉着）
    [string]$DistDir,
    [switch]$ReplaceAssets                             # 公开过的一版要换载荷，得有人当面点头
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $DistDir) { $DistDir = Join-Path $root "dist" }

$version = $Tag -replace '^v', ''
$uploads = @(
    @{ Name = "StarMark-$version-win-x64.zip"; Mime = "application/zip" },
    @{ Name = "update-manifest.json";          Mime = "application/json" },
    @{ Name = "update-manifest.sig";           Mime = "application/octet-stream" })
foreach ($u in $uploads) {
    if (-not (Test-Path (Join-Path $DistDir $u.Name))) { throw "少一颗：$($u.Name)（先跑 scripts\release-package.ps1）" }
}

$cfgPath = Join-Path $env:APPDATA "StarMark\github.json"
if (-not (Test-Path $cfgPath)) { throw "读不到应用的凭据：$cfgPath" }
$token = (Get-Content -Raw $cfgPath | ConvertFrom-Json).Token
if ([string]::IsNullOrWhiteSpace($token)) { throw "应用存的那把 Token 是空的" }
$headers = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "X-GitHub-Api-Version" = "2022-11-28" }
$api = "https://api.github.com/repos/$Repository"

Write-Host "==> 1/5 先问：$Tag 上现在挂着什么" -ForegroundColor Cyan
# 先问一次，而不是"直接建、撞了再说"：撞车时 API 回的那句里分不清"标签重了"还是"这把凭据没权限"，
# 而这一版要做的恰好是"重标签上那份坏载荷得换掉"——不知道它挂着什么就没法决定下一步做什么。
$existing = $null
try {
    $existing = Invoke-RestMethod -Method Get -Uri "$api/releases/tags/$Tag" -Headers $headers
} catch {
    $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    if ($code -eq 404) { $existing = $null }                       # 没这一版，正常往下建草稿
    elseif ($code -eq 401 -or $code -eq 403) {
        throw "这把 Token 读不到 Release（HTTP $code）：去 GitHub 把细粒度令牌的 Contents 改成 Read and write（只给这一个仓库就够）"
    } else { throw "查 $Tag 挂着什么失败（HTTP $code）：$($_.ErrorDetails.Message)" }
}
if ($existing -and -not $existing.draft -and -not $ReplaceAssets) { throw "$Tag 已经是公开的一版（上面挂着 $($existing.assets.Count) 颗资产），换掉它就是改变别人机器上下一发检查会拿到的东西：确认要换就带上 -ReplaceAssets 重跑" }

if (-not $existing) {
    Write-Host "    那个标签还没有 Release：建草稿" -ForegroundColor DarkGray
    $body = @{ tag_name = $Tag; name = "StarMark $Tag"; body = "桌面版 $version：框架依赖，需 .NET 9 Desktop Runtime。"; draft = $true; prerelease = $false } | ConvertTo-Json
    try {
        $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType "application/json" -Body $body
    } catch {
        # 三种失败说的话不一样，混成一句就会指错方向（这一版真踩到过一次：403 权限问题被报成了"标签已存在"）：
        #   401/403 ⇒ 这把凭据没有建仓 Release 的权限，要去 GitHub 上调，不是重跑一次能好的；
        #   422     ⇒ 那个标签已经有 Release 了（上面的查询与建草稿之间被人抢先），覆盖它必须人来确认；
        #   其它    ⇒ 照实说，并把 API 的原话带上。
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        $detail = $_.ErrorDetails.Message
        if ($code -eq 401 -or $code -eq 403) {
            throw "这把 Token 没有建仓 Release 的权限（HTTP $code）：去 GitHub 把细粒度令牌的 Contents 改成 Read and write（只给这一个仓库就够）。API 原话：$detail"
        }
        throw "建草稿失败（HTTP $code）：$detail —— 若那个标签已经有 Release，别覆盖它：那会改变别人机器上已经装到的一版"
    }
} else {
    $release = $existing
    Write-Host ("    那个标签已经有 Release：沿用这条（上面已挂 {0} 颗资产）" -f $release.assets.Count) -ForegroundColor DarkGray
}

Write-Host "==> 2/5 先把新字节挂成临时名，三颗都传成了才撤旧名、归位" -ForegroundColor Cyan
# upload_url 是 GitHub 的 **URI 模板**（结尾带 {?name,label}），不是能直接拼参数的地址：
# 原样拼上 "?name=..." 打出去的是畸形请求，而 API 回的那句是"Multipart form data required"——
# 它说的是另一件事（以为你在发表单），照着那句去改表单会一路走偏。所以先把模板那段大括号摘掉，
# 再自证一句"确实摘掉了"：这条路上任何一次静默拼错，报出来的都是不相干的原因。
$uploadBase = $release.upload_url -replace '\{[^}]*\}$', ''
if ($uploadBase -eq $release.upload_url) { throw "upload_url 结尾不是 URI 模板那对大括号，不敢猜该怎么拼：$($release.upload_url)" }
# 为什么绕这一圈临时名：同一条 Release 上两颗同名资产会被拒（实测 already_exists），换载荷必须先撤后挂；
# 而"撤了再传"意味着一次 72 MB 的上传失败就当颗少一颗可下载的文件——这一次真踩到了（地址拼错，旧 zip 已撤、新 zip 没上去）。
# 挂临时名不需要撤任何东西，所以传砸的时候旧的三颗一颗没动，那一版还是它原来的载荷。
$suffix = ".uploading-$PID"
$staged = @()
try {
    foreach ($u in $uploads) {
        $staged += Invoke-RestMethod -Method Post -Uri ($uploadBase + "?name=$($u.Name)$suffix") `
            -Headers $headers -ContentType $u.Mime -InFile (Join-Path $DistDir $u.Name)
        Write-Host ("  临时名已挂上 {0}" -f $u.Name)
    }
} catch {
    foreach ($s in $staged) {
        try { Invoke-RestMethod -Method Delete -Uri $s.url -Headers $headers | Out-Null }
        catch { Write-Warning ("  临时那颗自己撤不掉，得人工去删：{0}" -f $s.name) }
    }
    throw "挂新字节这一步就失败了，没有动旧资产（那一版还是它原来的载荷）：$($_.Exception.Message)"
}
# 到这里新字节已经在服务器上了，剩下的只是几次短请求
$remote = @{}
foreach ($a in @($release.assets)) { $remote[$a.name] = $a }
foreach ($u in $uploads) {
    if ($remote.ContainsKey($u.Name)) {
        Invoke-RestMethod -Method Delete -Uri $remote[$u.Name].url -Headers $headers | Out-Null
        Write-Host ("  撤下旧的 {0}" -f $u.Name)
    }
}
foreach ($s in $staged) {
    $finalName = $s.name -replace [regex]::Escape($suffix), ''
    Invoke-RestMethod -Method Patch -Uri ($api + "/releases/assets/$($s.id)") -Headers $headers -ContentType "application/json" `
        -Body (@{ name = $finalName } | ConvertTo-Json) | Out-Null
    Write-Host ("  已归位 {0}" -f $finalName)
}

Write-Host "==> 3/5 从 API 读回来对账（远端给了摘要就比 sha256，没给才退回比字节数）" -ForegroundColor Cyan
# 这一步是"我传上去的字节＝别人下载到的字节"唯一的自证机会。摘要比字节数强：
# 换一次签名出来的串**和原来一样长**，只比长度的对账会把旧内容读成"没变"（v1.0.0 那次就是这样）。
$fresh = Invoke-RestMethod -Method Get -Uri "$api/releases/$($release.id)" -Headers $headers
$byName = @{}
foreach ($a in $fresh.assets) { $byName[$a.name] = $a }
foreach ($u in $uploads) {
    $path = Join-Path $DistDir $u.Name
    if (-not $byName.ContainsKey($u.Name)) { throw "远端没有这颗：$($u.Name)（草稿留着，别当发出去了）" }
    $asset = $byName[$u.Name]
    if ($asset.digest) {
        $sha = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
        if ($asset.digest -ne "sha256:$sha") { throw "这颗内容与远端摘要对不上：$($u.Name) 本地 sha256:$sha / 远端 $($asset.digest)" }
    } else {
        # 摘要不是保证有的（GitHub 还没算出来、或这颗不是走 API 传的），那种时候只能退到长度——
        # 退到哪一档要说出来，否则这句"对上了"给人的把握比它实际证到的多。
        Write-Warning ("  {0} 远端没给摘要，这一颗只比对字节数" -f $u.Name)
        if ([int64]$asset.size -ne (Get-Item $path).Length) { throw "这颗字节数对不上：$($u.Name) 本地 $((Get-Item $path).Length) / 远端 $($asset.size)" }
    }
}
Write-Host ("  三颗已在，内容与本地一致（载荷 {0:N0} 字节）" -f [int64]$byName["StarMark-$version-win-x64.zip"].size)

Write-Host "==> 4/5 证据齐了才公开" -ForegroundColor Cyan
Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType "application/json" `
    -Body (@{ draft = $false } | ConvertTo-Json) | Out-Null

Write-Host "==> 5/5 再读一次那条 Release（界面上给的就是这个地址）" -ForegroundColor Cyan
$done = Invoke-RestMethod -Method Get -Uri "$api/releases/$($release.id)" -Headers $headers
if ($done.draft) { throw "转正式没生效：$Tag 还是草稿（装了的人看不见它）" }
Write-Host ""
Write-Host "已发布：$($done.html_url)"
Write-Host "装着的程序下一发检查就会看见 $Tag；界面上那颗「立即更新」从此有东西可下。"
