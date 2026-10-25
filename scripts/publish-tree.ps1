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

Write-Host "==> 1/4 dotnet publish StarMark.UI (Release x64，框架依赖)" -ForegroundColor Cyan
dotnet publish "$src\src\StarMark.UI" -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish StarMark.UI 失败（退出码 $LASTEXITCODE）" }

Write-Host "==> 2/4 dotnet publish StarMark.Updater -> Updater\（整目录，不是只搬一颗 exe）" -ForegroundColor Cyan
# 更新器要它自己的 .dll、runtimeconfig.json 和它引用的那几颗程序集；按清单逐颗复制就会漏，
# 而"漏一颗"只在用户机器上出现（UpdaterPaths.InInstallDir 那一侧复制的是整目录，来源必须也是整目录）。
dotnet publish "$src\src\StarMark.Updater" -c Release -p:Platform=x64 -r win-x64 --self-contained false `
    -o (Join-Path $OutDir "Updater")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish StarMark.Updater 失败（退出码 $LASTEXITCODE）" }

Write-Host "==> 3/4 当场自证这棵树完整" -ForegroundColor Cyan
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

# 第四样是那两颗图标（批次 VC）：运行时**从 BaseDirectory 按文件名读它们**（MainWindow 的 SetIcon、
# TrayHost 的 LoadTrayIcon），csproj 里它们是 Content 而不是嵌入资源——所以"构建 0 Warning、测试全绿"
# 完全可能配上一棵没有图标的树，而表现不是崩：标题栏回到通用图，托盘连一颗图标都不交（NIF_ICON 被省掉）。
# 上面 .xbf 那一节讲的正是同一形状（坑表 #237）：dotnet publish 天生会漏东西，这条链上一句错误都不会先喊出来。
# ⚠ "查"与"拒"必须写在**同一行**：ReleasePackagingGateTests 那句"只查不拒＝那句检查是装饰"是按行读的，
#   把 throw 挪到下一行它就红（那条口径今天仍然成立，所以改这里而不是改它）。
foreach ($ico in @("StarMark.ico", "StarMarkTray.ico")) {
    $p = Join-Path $OutDir $ico
    if (-not (Test-Path -LiteralPath $p)) { throw "树里没有 $p（图标不在树里＝装完没有那颗星；安装包读的也是树里这一份，ISCC 会在编译期一起停下）" }
}

Write-Host "产物树：$OutDir"
Write-Host "  主程序 : $entry"
Write-Host "  更新器 : $updater"
Write-Host "  界面   : $xbf + $pri（.xbf 共 $((Get-ChildItem -Recurse -Filter *.xbf -Path $OutDir).Count) 颗）"

# ---- 4/4 冻结清单（P-209）：把"这棵树里每颗字节"记成一份名字↔内容的账，三颗调用点拿它当凭据 ----
# 那笔账的起因：v1.0.3 的 stage\1.0.3 在发布之后被人重新产过一次树，事后量出 403 条目里 23 颗 CRC
# 不同——"审包认的字节"与"用户装到的字节"从那一刻起就不是同一批，而链上一句错误都没喊。
# 所以产树这一刻把整棵树冻结成 starmark-tree.json：版本（props 那颗唯一出处的第三位读者）、
# commit、每颗文件的相对路径（正斜杠，与更新链清单同一规范形）、sha256 与大小。
# 纪律：排序用 StringComparer::Ordinal（不随区域设置晃）；⛔ 任何时间戳——
# 同一棵树两次产出的清单必须逐字节相同，否则"重产一次"与"被人动过"就再也分不开。
Write-Host "==> 4/4 冻结这棵树的清单并当场读回自证（starmark-tree.json）" -ForegroundColor Cyan
$props = Get-Content -Raw (Join-Path $src "Directory.Build.props")
$matched = [regex]::Match($props, '<Version>([^<]+)</Version>')
if (-not $matched.Success) { throw "Directory.Build.props 里没有 <Version>，这棵树不知道自己是哪一版" }
$treeVersion = $matched.Groups[1].Value.Trim()
$commit = [string](git -C $src rev-parse HEAD)
if ($LASTEXITCODE -ne 0) { throw "git rev-parse 失败：清单里那颗 commit 读不到" }
$manifestName = "starmark-tree.json"
$manifestPath = Join-Path $OutDir $manifestName
$relPaths = [string[]]@()
foreach ($one in (Get-ChildItem -LiteralPath $OutDir -Recurse -File)) {
    $rel = $one.FullName.Substring($OutDir.Length + 1).Replace("\", "/")
    if (-not ($rel.Equals($manifestName))) { $relPaths += $rel }
}
[Array]::Sort($relPaths, [System.StringComparer]::Ordinal)
$entries = @()
foreach ($rel in $relPaths) {
    $full = Join-Path $OutDir $rel
    $entries += [ordered]@{
        path   = $rel
        sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        size   = (Get-Item -LiteralPath $full).Length
    }
}
$manifest = [ordered]@{
    version = $treeVersion
    commit  = $commit
    files   = $entries
}
[System.IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject $manifest -Depth 5) + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
# 读回自证走另一颗脚本（那把尺三颗调用点共用，不在这里再写一遍"枚举＋比对"——两处实现必漂移，
# #189/#193 那一族），产完立刻对一次：写清单与读清单各写各的话，"写歪了"要等发布那头才发现。
& (Join-Path $PSScriptRoot "verify-tree.ps1") -TreeDir $OutDir -ExpectVersion $treeVersion
if (-not $?) { throw "清单读回自证失败：这棵树的冻结清单与树本身对不上" }
Write-Host "  清单已冻结并读回自证：$($entries.Count) 颗文件、版本 $treeVersion、commit $commit"
