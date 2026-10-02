# 产出"这一版的那棵树"：主程序 + 更新器，各自完整、同一个目录。
# 用法：powershell -ExecutionPolicy Bypass -File scripts\publish-tree.ps1 -OutDir <绝对路径>
#
# 为什么单独一颗脚本，而不是在 publish.ps1 里多写一行：产出这棵树的人有**两个**
# （本机装一份的 publish.ps1、打发布 zip 的 release-package.ps1）。两处各写一遍 dotnet publish
# 就会漂成"本机那份带更新器、发出去的那份没带"——而那种产物的表现不是构建红，
# 是用户点「立即更新」时 UpdaterLauncher 在安装目录里找不到 Updater\，只得到一句"没能起来"（#189/#193 那一族）。
#
# 最后那两句 Test-Path 不是礼貌检查：一棵"缺主程序"或"缺更新器"的树一旦被 zip 打进发布产物，
# 修它的代价是"发一版没人能启动"或"这一版之后再也更新不了"，两种都只能靠下一版补救。
param(
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $PSScriptRoot
$OutDir = (New-Item -ItemType Directory -Force -Path $OutDir).FullName

Write-Host "==> 1/3 dotnet publish StarMark.UI (Release x64，框架依赖)" -ForegroundColor Cyan
dotnet publish "$src\src\StarMark.UI" -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish StarMark.UI 失败（退出码 $LASTEXITCODE）" }

Write-Host "==> 2/3 dotnet publish StarMark.Updater -> Updater\（整目录，不是只搬一颗 exe）" -ForegroundColor Cyan
# 更新器要它自己的 .dll、runtimeconfig.json 和它引用的那几颗程序集；按清单逐颗复制就会漏，
# 而"漏一颗"只在用户机器上出现（UpdaterPaths.InInstallDir 那一侧复制的是整目录，来源必须也是整目录）。
dotnet publish "$src\src\StarMark.Updater" -c Release -p:Platform=x64 -r win-x64 --self-contained false `
    -o (Join-Path $OutDir "Updater")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish StarMark.Updater 失败（退出码 $LASTEXITCODE）" }

Write-Host "==> 3/3 当场自证这棵树完整" -ForegroundColor Cyan
$entry = Join-Path $OutDir "StarMark.UI.exe"
if (-not (Test-Path $entry)) { throw "树里没有主程序：$entry" }
$updater = Join-Path $OutDir "Updater\StarMark.Updater.exe"
if (-not (Test-Path $updater)) { throw "树里没有更新器：$updater（这一版发出去就再也更新不了）" }
# 第三样是"看得见的界面"：非打包的 WinUI 3 靠 *.xbf（编译后的 XAML）与 StarMark.UI.pri（ms-appx 的索引）
# 找自己的每个页面，而 dotnet publish 天生不带它们（批次 UH 真踩到：构建 0 Warning、测试全绿、
# 从 bin 跑一切正常，唯独发布产物双击只会抛 XamlParseException —— 见坑表 #237）。
# 搬运那一步在 Directory.Build.targets 的 CopyCompiledXamlToPublishDirectory；这里只负责"没搬成就别出货"：
# 一棵起不来的树被当成更新包发出去，后果是用户连旧版都用不了，而这条链上没有任何一句错误会先喊出来。
$xbf = Join-Path $OutDir "App.xbf"
if (-not (Test-Path $xbf)) { throw "树里没有编译后的 XAML：$xbf（publish 少了 XAML 产物，装了也起不来）" }
$pri = Join-Path $OutDir "StarMark.UI.pri"
if (-not (Test-Path $pri)) { throw "树里没有资源索引：$pri（ms-appx 找不到的表现是某个页面打不开）" }

Write-Host "产物树：$OutDir"
Write-Host "  主程序 : $entry"
Write-Host "  更新器 : $updater"
Write-Host "  界面   : $xbf + $pri（.xbf 共 $((Get-ChildItem -Recurse -Filter *.xbf -Path $OutDir).Count) 颗）"
