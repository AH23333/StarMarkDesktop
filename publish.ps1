# StarMark 桌面版本地产物（改造提示词 Phase 5；批次 UH 起，这棵树由 scripts\publish-tree.ps1 一处产出）
# 用法：在仓库根目录以普通用户权限执行（无需管理员）
#   powershell -ExecutionPolicy Bypass -File publish.ps1
# 产物：publish\StarMark.UI.exe（框架依赖，需 .NET 9 Desktop Runtime）
#       publish\Updater\StarMark.Updater.exe —— 「立即更新」换文件靠的就是它，少带这一目录
#       就等于发出去之后再也更新不了（界面上只得到一句"更新器没能起来"）
#       桌面快捷方式「StarMark」指向该 exe

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

# 怎么产出这棵树（含更新器、含"少一颗就别往下走"那两句自证）只有 scripts\publish-tree.ps1 那一份；
# 这里不再自己写 dotnet publish，否则本机与发布两条路会漂成两棵不一样的树（见那颗脚本的注释）。
& (Join-Path $root "scripts\publish-tree.ps1") -OutDir (Join-Path $root "publish")
if (-not $?) { throw "产出树失败" }

Write-Host "==> 定位可执行文件与图标" -ForegroundColor Cyan
$exe = Join-Path $root "publish\StarMark.UI.exe"
if (-not (Test-Path $exe)) { throw "未找到发布产物: $exe" }

# 图标：优先复用仓库内 .ico；没有则跳过（Windows 会用 exe 内嵌图标）
$icoCandidates = Get-ChildItem -Path $root -Recurse -Filter "*.ico" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|publish|dist)\\' } |
    Select-Object -First 1

Write-Host "==> 创建桌面快捷方式「StarMark」" -ForegroundColor Cyan
$desktop = [Environment]::GetFolderPath("Desktop")
$lnkPath = Join-Path $desktop "StarMark.lnk"
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($lnkPath)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = (Split-Path $exe)
$lnk.Description = "StarMark 桌面版 —— Stars / 书签 / 本地文件统一检索与桌面组件"
if ($icoCandidates) { $lnk.IconLocation = $icoCandidates.FullName }
$lnk.Save()

Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Write-Host "  产物     : $exe"
Write-Host "  更新器   : $(Join-Path $root 'publish\Updater\StarMark.Updater.exe')"
Write-Host "  快捷方式 : $lnkPath"
if ($icoCandidates) { Write-Host "  图标     : $($icoCandidates.FullName)" }
Write-Host ""
Write-Host "要打成发得出去的三颗（zip + 清单 + 签名）：powershell -ExecutionPolicy Bypass -File scripts\release-package.ps1"
Write-Host "提示：开机自启可在应用内 设置 → 组件 → 「开机自动启动」开启（HKCU Run，无需管理员）。"
