# 打出"发得出去的那三颗"：载荷 zip + update-manifest.json + update-manifest.sig（批次 UH）。
# 用法：在仓库根目录以普通用户权限执行（无需管理员）
#   powershell -ExecutionPolicy Bypass -File scripts\release-package.ps1
#   可选：-OutDir <目录>（默认 <仓库根>\dist）
#
# 三颗缺一颗，这条链就在用户机器上少一步，而且少的那一步说的还是别的错：
#   少 zip      ⇒ 点「立即更新」只会得到"这一版没带更新包"。
#   少清单/签名 ⇒ 探测能报"有新版"（那一发只看标签），但审包必须拒：没有可信清单就没有任何字节可信。
# 所以这里三颗一起产、一起点名，并把标签一起打出来给人推。
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
Write-Host "==> 1/4 版本 $version（标签 $tag）" -ForegroundColor Cyan

# ---- 2. 一棵干净的树：不带上一版的残渣 ----
# 逐颗对账能签掉"包里有、清单没列"，但签不掉"这一版其实已经不需要这颗文件了"——
# 那种残渣会跟着 zip 一直活下去，而没人会去查它为什么在。所以每次都从空目录产。
$stage = Join-Path $OutDir "stage\$version"
$stageParent = Split-Path -Parent $stage
if ((Split-Path -Leaf $stageParent) -ne "stage") { throw "拒绝删除不在 dist\stage 下的路径：$stage" }
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

# 怎么产出这棵树只有 scripts\publish-tree.ps1 那一份（本机装的 publish.ps1 走的是同一颗）。
Write-Host "==> 2/4 产出一棵干净的树（含 Updater\）" -ForegroundColor Cyan
& (Join-Path $root "scripts\publish-tree.ps1") -OutDir $stage
if (-not $?) { throw "产出树失败" }

# ---- 3. zip：树里的东西全进包，路径以树根为基准（清单与摊包两侧都按这个基准算） ----
Write-Host "==> 3/4 打包 $zipName" -ForegroundColor Cyan
$zipPath = Join-Path $OutDir $zipName
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
if (-not $?) { throw "打包失败" }

# ---- 4. 清单 + 签名：算哈希、签、并用内嵌那把公钥自验一遍 ----
# 私钥刻意不入库，缺省位置 %USERPROFILE%\.starmark\update-signing.pk8.pem。
# 这一步**拒绝出东西**的两种情况都必须当失败看：私钥与源码里的公钥不是一对（发出去每一版所有安装都拒），
# 以及包里没有主程序（换完就没有能启动的东西）。
Write-Host "==> 4/4 签清单" -ForegroundColor Cyan
dotnet run --project (Join-Path $root "tools\StarMark.UpdateSigner") -c Release -- $zipPath $version $OutDir
if ($LASTEXITCODE -ne 0) { throw "签清单失败（退出码 $LASTEXITCODE）：这一版没签出来，别当成发出去了" }

Write-Host ""
Write-Host "三颗已就位（建 Release 时全部要传，名字一个都不能改）：" -ForegroundColor Green
foreach ($f in @($zipName, "update-manifest.json", "update-manifest.sig")) {
    $p = Join-Path $OutDir $f
    if (-not (Test-Path $p)) { throw "说好了要产 $f，可它不在 $OutDir" }
    Write-Host ("  {0,-28} {1,10:N0} 字节" -f $f, (Get-Item $p).Length)
}
Write-Host ""
Write-Host "接下来（发布那一步由人推，这里只把该敲的命令念出来）："
Write-Host "  git tag $tag; git push origin $tag"
Write-Host "  建 Release：标签 $tag，把上面三颗作为资产一并上传"
