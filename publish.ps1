# StarMark 桌面版发布脚本（改造提示词 Phase 5）
# 用法：在仓库根目录以普通用户权限执行（无需管理员）
#   powershell -ExecutionPolicy Bypass -File publish.ps1
# 产物：publish\StarMark.UI.exe（框架依赖，需 .NET 9 Desktop Runtime）
#       桌面快捷方式「StarMark」指向该 exe

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

Write-Host "==> 1/3 dotnet publish (Release x64)" -ForegroundColor Cyan
dotnet publish "$root\src\StarMark.UI" -c Release -p:Platform=x64 -r win-x64 --self-contained false -o "$root\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }

Write-Host "==> 2/3 定位可执行文件与图标" -ForegroundColor Cyan
$exe = Join-Path $root "publish\StarMark.UI.exe"
if (-not (Test-Path $exe)) { throw "未找到发布产物: $exe" }

# 图标：优先复用仓库内 .ico；没有则跳过（Windows 会用 exe 内嵌图标）
$icoCandidates = Get-ChildItem -Path $root -Recurse -Filter "*.ico" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|publish)\\' } |
    Select-Object -First 1

Write-Host "==> 3/3 创建桌面快捷方式「StarMark」" -ForegroundColor Cyan
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
Write-Host "  快捷方式 : $lnkPath"
if ($icoCandidates) { Write-Host "  图标     : $($icoCandidates.FullName)" }
Write-Host ""
Write-Host "提示：开机自启可在应用内 设置 → 组件 → 「开机自动启动」开启（HKCU Run，无需管理员）。"
