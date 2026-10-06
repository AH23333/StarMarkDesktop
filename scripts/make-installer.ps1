# 造那颗 setup.exe（批次 VB；✅P-166：Inno Setup 造、只作首装入口、载荷维持 framework-dependent）。
#
# 用法（仓库根目录）：
#   powershell -ExecutionPolicy Bypass -File scripts\make-installer.ps1
#   可选：-SourceDir <树>（默认 dist\stage\<版本>，就是 release-package 用来打 zip 的那一棵）
#         -OutDir <目录>（默认 dist）  -IsccPath <ISCC.exe>  -Pin <sha256>
#
# 三件事在这颗脚本里，别挪到 .iss 里去做：
#  1. **找 ISCC**：仓内 tools\InnoSetup6 → 注册表卸载项 → 两处常见安装目录 → PATH。
#  2. 找不到才下载：只从官方 GitHub release 地址取，且**必须**两道都过——
#     Authenticode 签名状态 Valid ＋ 签名者 CN 含 Pyrsys B.V.（官方页面对外说明的就是这个发布者名），
#     以及 sha256 与钉住的那一颗一致。签名只证"没被改过、来自该发布者"，钉哈希才证"我们认这一版"，
#     两者缺一都拦不住"官方源上换了一个我没打算用的版本"。
#  3. 装到 **D 盘**：安装位置由用户 2026-10-04 指定（「允许下载第三方工具，但安装位置要放在D盘」），
#     所以这里对解析出来的绝对路径断言盘符，不是 D 就当场拒绝——不写机器级注册表也不改 PATH，删掉那个目录就干净。
#
# ⚠ 编码：这颗 .ps1 与 scripts\installer\StarMark.iss 都必须 UTF-8 **带 BOM** ＋ CRLF（PowerShell 5.1 与 Inno 都按 ANSI 读无 BOM 的 UTF-8）。

[CmdletBinding()]
param(
    [string]$SourceDir,
    [string]$OutDir,
    [string]$IsccPath,
    [string]$Pin
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# ---- 0. 版本与树：都不自己算 ----
$props = Get-Content -Raw (Join-Path $root "Directory.Build.props")
$matched = [regex]::Match($props, '<Version>([^<]+)</Version>')
if (-not $matched.Success) { throw "Directory.Build.props 里没有 <Version>，这一版不知道该打成哪一串" }
$version = $matched.Groups[1].Value.Trim()
if (-not $OutDir) { $OutDir = Join-Path $root "dist" }
$OutDir = (New-Item -ItemType Directory -Force -Path $OutDir).FullName
if (-not $SourceDir) { $SourceDir = Join-Path $root "dist\stage\$version" }
$SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path
Write-Host "==> 1/4 版本 $version；载荷树 $SourceDir" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath (Join-Path $SourceDir "StarMark.UI.exe"))) {
    throw "那棵树里没有入口 StarMark.UI.exe（$SourceDir）。先跑 scripts\publish-tree.ps1 -OutDir $SourceDir —— 没有入口的包装完就是双击没反应"
}
if (-not (Test-Path -LiteralPath (Join-Path $SourceDir "Updater\StarMark.Updater.exe"))) {
    throw "那棵树里没有 Updater\ 整目录（$SourceDir）。少了它，装完之后用户**永远没法自动更新**（UH 那条老账就是这么来的）"
}

# ---- 1. 找 ISCC ----
function Find-Iscc {
    param([string]$Hint)
    $cands = @()
    if ($Hint) { $cands += $Hint }
    $cands += (Join-Path $root "tools\InnoSetup6\ISCC.exe")
    foreach ($key in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1")) {
        $loc = (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue).InstallLocation
        if ($loc) { $cands += (Join-Path $loc "ISCC.exe") }
    }
    $cands += "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    $cands += "C:\Program Files\Inno Setup 6\ISCC.exe"
    foreach ($c in $cands) {
        if ($c -and (Test-Path -LiteralPath $c -ErrorAction SilentlyContinue)) { return (Resolve-Path -LiteralPath $c).Path }
    }
    $onPath = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

$iscc = Find-Iscc -Hint $IsccPath

# ---- 2. 没有就从官方源取，两道校验缺一不可 ----
$InstallerUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe"
$ExpectedSigner = "Pyrsys B.V."          # 官方页面对外说明的发布者名（不是 "Jordan Russell"，别看产品名猜）
# 钉住的哈希：2026-10-04 由"已验签的那一次下载"算出来后写回这里（签名者 CN=Pyrsys B.V. 同场过了）。
# 占位串**故意让它不可用**——宁可当场拒绝，也不许"这次先放过"变成"以后都放过"（#258 那一族：没验过 != 验过）。
$PinnedSha256 = "9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732"

if (-not $iscc) {
    # 用户指定：第三方工具只能落在 D 盘。仓库本身在 D 上就装进仓内 tools\；
    # 换一台仓库在 C 上的机器也不能把它塞进系统盘，退到 D:\StarMarkTools\ ——两侧都在 D，且不进版本库。
    $installDir = Join-Path $root "tools\InnoSetup6"
    if ((Split-Path -Qualifier $installDir) -ne "D:") { $installDir = "D:\StarMarkTools\InnoSetup6" }
    $toolsParent = Split-Path -Parent $installDir
    $qualifier = Split-Path -Qualifier $installDir
    if ($qualifier -ne "D:") { throw "Inno Setup 的安装位置解析成了 '$qualifier'（$installDir），而第三方工具只允许装在 D 盘（用户 2026-10-04 指定）——这台机器上没有 D 盘，别把它装进系统盘" }

    $tmp = Join-Path $env:TEMP ("innosetup-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".exe")
    Write-Host "==> 2/4 本机没有 Inno Setup：下载官方安装包并验签（落到 $installDir）" -ForegroundColor Cyan
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $InstallerUrl -OutFile $tmp -UseBasicParsing -MaximumRedirection 5

        $sig = Get-AuthenticodeSignature -LiteralPath $tmp
        if ($sig.Status -ne "Valid") { throw "签名状态不是 Valid（$($sig.Status)：$($sig.StatusMessage)）—— 这个下载不能用" }
        if (-not $sig.SignerCertificate) { throw "没有签名者证书" }
        if ($sig.SignerCertificate.Subject -notlike "*$ExpectedSigner*") {
            throw "签名者不是 $ExpectedSigner，而是：$($sig.SignerCertificate.Subject)"
        }
        if (-not $sig.TimeStamperCertificate) { Write-Host "  ! 无时间戳：签名者过期后这条验证会变红，属正常" -ForegroundColor DarkYellow }

        $hash = (Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "  签名者 $($sig.SignerCertificate.Subject)｜sha256 $hash"
        if ($Pin) { $PinnedSha256 = $Pin }
        if ($PinnedSha256 -eq "UNPINNED") {
            throw "sha256 还没钉住。已验签通过，哈希是 $hash —— 核对无误后用 -Pin $hash 跑一次，并把这颗常量写回本脚本（一次性的，之后每次构建都按它拒替换）"
        }
        if ($hash -ne $PinnedSha256.ToLowerInvariant()) { throw "sha256 与钉住的那颗不一致（拿到 $hash，钉的是 $PinnedSha256）—— 停，别装" }

        New-Item -ItemType Directory -Force -Path $toolsParent | Out-Null
        # /PORTABLE=1：只往 /DIR 落文件，不写注册表也不碰 PATH——上面那句"删掉那个目录就干净"靠它才成立。
        # ⚠ 这一发**会弹一次 UAC**：Inno 自家安装器写死了 PrivilegesRequired=admin，
        #   带上 /NOALLUSERS 也不认（2026-10-04 实测：进程挂在授权那一步，屏幕上一道框没人点就一直等）。
        #   所以这里不假装能静默到底，而是先把话说明白、再给它一个上限：超时就把进程树收掉并报出来，
        #   免得一次发版卡在"谁都看不见的一道框"上（#258 那一族：读不到结果不等于成功）。
        Write-Host "  下面会弹一道 UAC：那是**装 Inno Setup 这颗构建工具**（落 $installDir），不是 StarMark 本体——点『是』。" -ForegroundColor Yellow
        # ⚠ /DIR 的值**必须自己带引号**：Start-Process 的 -ArgumentList 不会替含空格的元素加引号，
        #   而仓库路径里就有空格（D:\Visual Studio Code\…）。不加引号它切在第一个空格处，
        #   安装器去建 "D:\Visual"，回的是 Error 183、退出码 4——那一发我把它读成了"那道 UAC 没人点"，
        #   连着追了三轮授权（坑表 #261：那句"多半是 UAC"是**在没有读数之前写好的诊断**，它替我把自己的引号 bug 归责成了用户的一次点击）。
        # 所以现在带 /LOG，并且失败时把日志尾巴贴出来：它一句话就能说出卡在哪一步。
        $toolLog = Join-Path $env:TEMP ("innosetup-install-" + [guid]::NewGuid().ToString("N").Substring(0, 8) + ".log")
        $silentArgs = @("/PORTABLE=1", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-",
                        "/DIR=`"$installDir`"", "/LOG=$toolLog")
        $proc = Start-Process -FilePath $tmp -ArgumentList $silentArgs -PassThru
        if (-not $proc.WaitForExit(240000)) {
            & taskkill /F /T /PID $proc.Id 2>$null | Out-Null
            throw "Inno Setup 的安装没在 4 分钟内结束（多半是那道 UAC 没被点）。工具没装上，这一版也就没造出安装包——别把它当成打好了包"
        }
        if ($proc.ExitCode -ne 0) {
            $why = ""
            if (Test-Path -LiteralPath $toolLog) {
                $why = "`n  安装日志最后几行：" + ((Get-Content -LiteralPath $toolLog -Tail 6) -join "`n                    ")
            }
            throw "Inno Setup 静默安装失败（退出码 $($proc.ExitCode)）$why"
        }
        $iscc = Find-Iscc -Hint $null
        if (-not $iscc) { throw "装完了却在 $installDir 找不到 ISCC.exe——别猜它去哪了，把上面的退出码贴出来再看" }
    } finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "==> 2/4 用现成的 ISCC：$iscc" -ForegroundColor Cyan
}

# ---- 3. 中文向导：有没有那颗语言包，看得见地说 ----
$isl = Join-Path (Split-Path -Parent $iscc) "Languages\ChineseSimplified.isl"
if (Test-Path -LiteralPath $isl) { Write-Host "  中文语言包在：向导走简体中文" }
else { Write-Host "  ! 没有简体中文语言包（Inno 官方不自带它）⇒ 这次编出来的向导是英文。不是失败，但别让它无声" -ForegroundColor DarkYellow }
$ja = Join-Path (Split-Path -Parent $iscc) "Languages\Japanese.isl"
if (Test-Path -LiteralPath $ja) { Write-Host "  日文语言包在（Inno 官方自带）：向导带日文一档，日语系统按 OS 语言进日文向导" }
else { Write-Host "  ! 没有日文语言包 ⇒ 这次的向导没有日文一档（日语系统会落到第一档语言）。不是失败，但别让它无声" -ForegroundColor DarkYellow }

# ---- 4. 编 ----
$iss = Join-Path $PSScriptRoot "installer\StarMark.iss"
Write-Host "==> 3/4 ISCC 编 $iss" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" "/DSourceDir=$SourceDir" "/DOutputDir=$OutDir" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC 失败（退出码 $LASTEXITCODE）" }

$setup = Join-Path $OutDir "StarMark-$version-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "ISCC 报了成功，可 $setup 不在——那句'成功'就不算数" }
$bytes = (Get-Item -LiteralPath $setup).Length
Write-Host ""
Write-Host "==> 4/4 setup.exe 已就位" -ForegroundColor Green
Write-Host ("  {0,-34} {1,10:N0} 字节  sha256 {2}" -f (Split-Path -Leaf $setup), $bytes, (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant())
Write-Host ""
Write-Host "它只是**首装入口**：之后的升级走应用内「立即更新」（zip + 签名清单 + 事务替换），不要把这颗塞进 update-manifest.json 的 files。"
Write-Host "传上远端 Release（第四颗产物）要单独点头：scripts\publish-release.ps1 已把它列进 $uploads 的第四颗，"
Write-Host "  公开之前会按名字与摘要读回来对账——少这一颗它自己就拒绝，不会默默发成一版没安装包的。"
