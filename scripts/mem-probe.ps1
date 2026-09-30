# 内存基线探针（批次 SS）。用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\mem-probe.ps1 -ExePath <...\StarMark.UI.exe> [-Scenario base|noWidgets] [-SettleSec 60]
# 它做四件事：
#   1) 把 %APPDATA%\StarMark 的档复制成一份沙盒（数据库／settings.json／widgets.json），进程用 STARMARK_DB_PATH 指过去；
#      真库与真配置一个字节都不动（脚本结束会打印沙盒里出现了 -wal 作为跑过副本的证据）。
#   2) 沙盒里必须关掉四把会删/写用户文件的开关（剪贴板历史／剪贴板图片采集／备份带图／自动备份）——
#      关不掉就直接拒绝启动：拿副本把他真图缩略图删掉，那不是取证，那是事故。
#   3) 起进程、等主窗句柄、按节拍采样工作集/私有字节/句柄/线程数，结束时只杀**自己起的**那个 pid。
#   4) 从真日志里把这一次会话的 [启动]/[内存] 行摘出来——进程内读数（托管堆、程序集数）只有那里有。
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [ValidateSet('base', 'noWidgets')][string]$Scenario = 'base',
    [int]$SettleSec = 60,
    [int]$SampleEverySec = 10
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) { throw "找不到要量的产物：$ExePath" }

$srcProfile = Join-Path $env:APPDATA 'StarMark'
$sandbox = Join-Path $env:TEMP ('StarMarkMemProbe\' + $Scenario)
$logDir = Join-Path $env:LOCALAPPDATA 'StarMark\logs'
$stamp = Get-Date
$rowFile = Join-Path $sandbox 'rows.csv'

if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $sandbox | Out-Null

foreach ($name in @('starmark.db', 'starmark.db-wal', 'starmark.db-shm', 'settings.json', 'widgets.json', 'rss-cache.json')) {
    $from = Join-Path $srcProfile $name
    if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $sandbox $name) -Force }
}

$settingsPath = Join-Path $sandbox 'settings.json'
$risky = @('ClipboardHistoryEnabled', 'ClipboardImageEnabled', 'BackupClipboardImagesEnabled', 'AutoBackupEnabled')
if (Test-Path -LiteralPath $settingsPath) {
    $text = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
} else {
    $text = '{ }'
}
$doc = $text | ConvertFrom-Json
foreach ($key in $risky) {
    if ($doc.PSObject.Properties[$key]) { $doc.$key = $false } else { $doc | Add-Member -NotePropertyName $key -NotePropertyValue $false }
}
$doc | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

# 读回来验一遍：写进去没生效，等于让探针去动他的真图目录
$back = (Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8) | ConvertFrom-Json
foreach ($key in $risky) {
    if ($back.$key -ne $false) { throw "沙盒里 $key 没能关掉，拒绝启动（宁可不出数，也不拿他的真目录跑）" }
}

$widgetsPath = Join-Path $sandbox 'widgets.json'
# 场景 noWidgets：把沙盒档里的实例清空，量"一颗组件窗都不建"要多少钱。
# ⚠ 不要走 ShowOnStartup 那条路：v3 的 WidgetInstanceConfig 里没有这个字段（它只是 v1 迁移的历史名），
#   改它等于什么都没改——两次跑的数字会被当成"组件不要钱"的证据，那是假证据（坑表 #185 同一族）。
$instances = 0
if (Test-Path -LiteralPath $widgetsPath) {
    $w = Get-Content -LiteralPath $widgetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($w.PSObject.Properties['Instances'] -and $w.Instances) { $instances = @($w.Instances).Count }
}
Write-Output ('沙盒里的组件实例数=' + $instances)
if ($Scenario -eq 'noWidgets' -and (Test-Path -LiteralPath $widgetsPath)) {
    $w.Instances = @()
    $w | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $widgetsPath -Encoding UTF8
    $check = (Get-Content -LiteralPath $widgetsPath -Raw -Encoding UTF8) | ConvertFrom-Json
    if (@($check.Instances).Count -ne 0) { throw '沙盒里的实例没能清空，拒绝启动（否则两个场景是同一份配置）' }
    Write-Output ('scenario=noWidgets：已从沙盒档删掉 ' + $instances + ' 个实例（真档未动）')
} elseif ($Scenario -eq 'noWidgets') {
    throw '沙盒里没有 widgets.json，noWidgets 场景与 base 会是同一份配置——不出这种数'
}

$logFile = Join-Path $logDir ('starmark-' + $stamp.ToString('yyyyMMdd') + '.log')
$logMark = 0
if (Test-Path -LiteralPath $logFile) { $logMark = (Get-Item -LiteralPath $logFile).Length }

$env:STARMARK_DB_PATH = (Join-Path $sandbox 'starmark.db')
$started = Get-Date
$proc = Start-Process -FilePath $ExePath -PassThru
Write-Output ("已起进程 pid=" + $proc.Id + " 沙盒库=" + $env:STARMARK_DB_PATH)

$deadline = (Get-Date).AddSeconds(30)
$seenWindow = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.HasExited) { break }
    if ($proc.MainWindowHandle -ne 0) { $seenWindow = $true; break }
}

function Sample([string]$label, [int]$elapsed) {
    $proc.Refresh()
    if ($proc.HasExited) { Write-Output ($label + '：进程已退出，无法采样'); return }
    $threads = 0
    try { $threads = $proc.Threads.Count } catch { }
    $line = ('{0}|{1}|{2}|{3}|{4}|{5}' -f $elapsed, $label,
        [math]::Round($proc.WorkingSet64 / 1MB, 1), [math]::Round($proc.PrivateMemorySize64 / 1MB, 1), $proc.HandleCount, $threads)
    Add-Content -LiteralPath $rowFile -Value $line
    Write-Output $line
}

if ($proc.HasExited) {
    Write-Output ('进程在 ' + [math]::Round(((Get-Date) - $started).TotalSeconds, 1) + ' 秒时就退出了（多半是他机器上已有一个实例攥着单实例锁）——本次不出数')
} else {
    if (-not $seenWindow) { Write-Output '30 秒内没等到主窗句柄：读数以"没起来"对待，别当基线用' }
    'elapsed|label|工作集MB|私有MB|句柄|线程' | Set-Content -LiteralPath $rowFile -Encoding UTF8
    Start-Sleep -Seconds 3
    Sample '起窗后3秒' 3
    $t = 3
    while ($t -lt $SettleSec) {
        $step = if ($SampleEverySec -gt 0) { $SampleEverySec } else { 10 }
        Start-Sleep -Seconds $step
        $t = $t + $step
        Sample ('静置' + $t + '秒') $t
    }
}

$pid2 = $proc.Id
try {
    $mine = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
    if ($mine -and $mine.StartTime -ge $started.AddSeconds(-5)) { $mine | Stop-Process -Force }
    elseif ($mine) { Write-Output ('pid=' + $pid2 + ' 的 StartTime 早于本次启动，不是我这个进程，不动它') }
} catch { Write-Output ('收尾停进程时出错（可能它已经自己退了）：' + $_.Exception.Message) }

$wal = Join-Path $sandbox 'starmark.db-wal'
Write-Output ('副本确实在跑的证据：沙盒里 -wal 存在=' + (Test-Path -LiteralPath $wal))

if (Test-Path -LiteralPath $logFile) {
    Write-Output '---- 这一次会话在日志里的分段读数（进程内：托管堆／程序集数只有这里有） ----'
    $lines = Get-Content -LiteralPath $logFile -Encoding UTF8
    $from = [array]::IndexOf($lines, ($lines | Where-Object { $_ -match ('会话开始 pid=' + $pid2 + '\b') } | Select-Object -First 1))
    if ($from -ge 0) {
        $lines[$from..($lines.Count - 1)] | Where-Object { $_ -match '\[内存\]|\[启动\]|\[耗时\]|自动备份|Ditto|二次启动' } | Select-Object -First 60 | ForEach-Object { Write-Output $_ }
    } else {
        Write-Output ('日志里没找到 pid=' + $pid2 + ' 的会话开始行——读数不采信，先查日志目录/滚动')
    }
}
Write-Output ('⚠ 这是一次被强杀的采样进程：日志里会有"有会话开始、无进程退出"，那是我的探针，不是他的崩溃（pid=' + $pid2 + '，时间窗 ' + $started.ToString('HH:mm:ss') + ' 起）')
