# 打出"发得出去的那四颗"：载荷 zip + update-manifest.json + update-manifest.sig + setup.exe（批次 UH 立前三颗，批次 VB 加第四颗）。
# 用法：在仓库根目录以普通用户权限执行（首次要装 Inno Setup 时会弹一次 UAC，那一发的是构建工具，不是本程序）
#   powershell -ExecutionPolicy Bypass -File scripts\release-package.ps1
#   可选：-OutDir <目录>（默认 <仓库根>\dist）
#
# 缺一颗，这条链就在用户机器上少一步，而且少的那一步说的还是别的错：
#   少 zip      ⇒ 点「立即更新」只会得到"这一版没带更新包"。
#   少清单/签名 ⇒ 探测能报"有新版"（那一发只看标签），但审包必须拒：没有可信清单就没有任何字节可信。
#   少 setup.exe ⇒ 自更新一点都不坏（链上那三节齐着），坏的是**没装过的人**：他手上只有一个 zip，
#                  没有开始菜单项、没有卸载入口——✅P-166 定下来的"首装走安装包"就落空了。
# 所以这里四颗一起产、一起点名，并把该敲的那两句念出来（发 Release 由 scripts\publish-release.ps1 做）。
#
# 版本号**从 Directory.Build.props 读**，不在这里写第二份：产物里露的是那一颗
# （AppVersion 读的是入口程序集的 InformationalVersion），这里若另写一串，
# 签出来的清单与用户手上那一版的标签就对不上——表现是"检查到新版、点下去说版本对不上"，
# 而构建与测试全绿，没人红（#189/#193 那一族：两处实现必漂移）。
param(
    [string]$OutDir
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root "dist" }
$OutDir = (New-Item -ItemType Directory -Force -Path $OutDir).FullName

# ---- 1. 版本与标签：唯一出处是 Directory.Build.props 的那一颗 <Version> ----
$props = Get-Content -Raw (Join-Path $root "Directory.Build.props")
$matched = [regex]::Match($props, '<Version>([^<]+)</Version>')
if (-not $matched.Success) { throw "Directory.Build.props 里没有 <Version>，这一版不知道该打成哪一串" }
$version = $matched.Groups[1].Value.Trim()
$tag = "v$version"                       # 口径：标签 = v + props 那一串（AppVersion 认 v 前缀）
$zipName = "StarMark-$version-win-x64.zip"   # 名字由版本算，不由这里自由发挥（UpdateAssets.PackageNameFor 同一口径）
$setupName = "StarMark-$version-setup.exe"   # 第四颗也是同一口径：发布那一侧与 make-installer 各算一次，必须算出同一个名字（有闸门钉着）
Write-Host "==> 1/5 版本 $version（标签 $tag）" -ForegroundColor Cyan

# ---- 2. 一棵干净的树：不带上一版的残渣 ----
# 逐颗对账能签掉"包里有、清单没列"，但签不掉"这一版其实已经不需要这颗文件了"——
# 那种残渣会跟着 zip 一直活下去，而没人会去查它为什么在。所以每次都从空目录产。
$stage = Join-Path $OutDir "stage\$version"
$stageParent = Split-Path -Parent $stage
if ((Split-Path -Leaf $stageParent) -ne "stage") { throw "拒绝删除不在 dist\stage 下的路径：$stage" }
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

# 怎么产出这棵树只有 scripts\publish-tree.ps1 那一份（本机装的 publish.ps1 走的是同一颗）。
Write-Host "==> 2/5 产出一棵干净的树（含 Updater\）" -ForegroundColor Cyan
& (Join-Path $root "scripts\publish-tree.ps1") -OutDir $stage
if (-not $?) { throw "产出树失败" }

# ---- 3. zip：树里的东西全进包，路径以树根为基准（清单与摊包两侧都按这个基准算） ----
Write-Host "==> 3/5 打包 $zipName" -ForegroundColor Cyan
$zipPath = Join-Path $OutDir $zipName
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
if (-not $?) { throw "打包失败" }

# ---- 4. setup.exe：同一棵树换个包装，**不重新 publish** ----
# 为什么让它吃第 2 步那棵树而不是自己再产一棵：两处编译必漂移（#189/#193 那一族），
# 而这里的漂移最阴——zip 与安装包都是"这一版"，里面却不是同一批字节，用户从哪条路装进来决定了他手上是哪一份。
# ⚠ 这颗**不进清单的 files**：它是首装入口（✅P-166 第三条），自更新仍走 zip＋验签＋事务替换那条链，一行不动。
Write-Host "==> 4/5 造安装包 $setupName" -ForegroundColor Cyan
& (Join-Path $root "scripts\make-installer.ps1") -SourceDir $stage -OutDir $OutDir
# 这里读 $? 而不是 $LASTEXITCODE：被 & 的是**另一颗 .ps1**，它自己内部的退出码归它管（那里对着 ISCC 查过了），
# 传回这一侧的只有"抛没抛"。按 $LASTEXITCODE 数会把上一颗原生程序的旧读数当成本步的结果——
# 那正好是"造包失败却报四颗已就位"的的形状。
if (-not $?) { throw "造安装包失败：这一版没有首装入口，别当成四颗都齐了" }

# ---- 5. 清单 + 签名：算哈希、签、并用内嵌那把公钥自验一遍 ----
# 私钥刻意不入库，缺省位置 %USERPROFILE%\.starmark\update-signing.pk8.pem。
# 这一步**拒绝出东西**的两种情况都必须当失败看：私钥与源码里的公钥不是一对（发出去每一版所有安装都拒），
# 以及包里没有主程序（换完就没有能启动的东西）。
Write-Host "==> 5/5 签清单" -ForegroundColor Cyan
dotnet run --project (Join-Path $root "tools\StarMark.UpdateSigner") -c Release -- $zipPath $version $OutDir
if ($LASTEXITCODE -ne 0) { throw "签清单失败（退出码 $LASTEXITCODE）：这一版没签出来，别当成发出去了" }

Write-Host ""
Write-Host "四颗已就位（建 Release 时全部要传，名字一个都不能改）：" -ForegroundColor Green
foreach ($f in @($zipName, "update-manifest.json", "update-manifest.sig", $setupName)) {
    $p = Join-Path $OutDir $f
    if (-not (Test-Path $p)) { throw "说好了要产 $f，可它不在 $OutDir" }
    Write-Host ("  {0,-28} {1,10:N0} 字节" -f $f, (Get-Item $p).Length)
}
Write-Host ""
Write-Host "接下来（标签与 Release 都由 scripts\publish-release.ps1 发；它在公开之前会把四颗读回来比内容）："
Write-Host "  git push origin $tag        （标签还没推过才需要；已发出去的那版不追改标签）"
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1 -Tag $tag"
Write-Host "  那一版已经公开过、要换载荷时再加 -ReplaceAssets（不给这句它会拒绝：那会改变别人机器上下一发检查拿到的东西）"
