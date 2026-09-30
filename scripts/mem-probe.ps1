# 内存基线探针（批次 SS）。用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\mem-probe.ps1 -ExePath <...\StarMark.UI.exe> [-Scenario base|noWidgets] [-KeepInstances N] [-SettleSec 60]
# 它做四件事：
#   1) 把 %APPDATA%\StarMark 的档复制成一份沙盒（数据库／settings.json／widgets.json），进程用 STARMARK_DB_PATH 指过去；
#      真库与真配置一个字节都不动（脚本结束会打印沙盒里出现了 -wal 作为跑过副本的证据）。
#   2) 沙盒里必须关掉四把会删/写用户文件的开关（剪贴板历史／剪贴板图片采集／备份带图／自动备份）——
#      关不掉就直接拒绝启动：拿副本把他真图缩略图删掉，那不是取证，那是事故。
#   3) 起进程、等主窗句柄、按节拍采样工作集/私有字节/句柄/线程数，结束时只杀**自己起的**那个 pid。
#   4) 从真日志里把这一次会话的 [启动]/[内存] 行摘出来——进程内读数（托管堆、程序集数）只有那里有。
#
# -KeepInstances N（批次 SU 加，P-137 的边际曲线）：只把**沙盒副本**里的组件实例削到前 N 颗，真档不动。
#   写回后一定读回来验数量，凑不出 N 颗就直接抛——"场景其实是空操作"比没数更坏（坑表 #185/#218 同一族）。
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [ValidateSet('base', 'noWidgets')][string]$Scenario = 'base',
    [int]$SettleSec = 60,
    [int]$SampleEverySec = 10,
    [int]$KeepInstances = -1
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) { throw "找不到要量的产物：$ExePath" }

$srcProfile = Join-Path $env:APPDATA 'StarMark'
$sandboxTag = if ($KeepInstances -ge 0) { $Scenario + '-' + $KeepInstances } else { $Scenario }
$sandbox = Join-Path $env:TEMP ('StarMarkMemProbe\' + $sandboxTag)
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
# 削实例数＝量"每颗组件窗值多少钱"的唯一可靠办法：同一份副本、同一个产物，只差实例数。
# ⚠ 不要走 ShowOnStartup 那条路：v3 的 WidgetInstanceConfig 里没有这个字段（它只是 v1 迁移的历史名），
#   改它等于什么都没改——两次跑的数字会被当成"组件不要钱"的证据，那是假证据（坑表 #185 同一族）。
$instances = 0
$widgetDoc = $null
if (Test-Path -LiteralPath $widgetsPath) {
    $widgetDoc = Get-Content -LiteralPath $widgetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($widgetDoc.PSObject.Properties['Instances'] -and $widgetDoc.Instances) { $instances = @($widgetDoc.Instances).Count }
}
Write-Output ('沙盒标签=' + $sandboxTag + '，副本里的组件实例数=' + $instances)
# 一个漏斗管两种"削实例"的写法：noWidgets 等价于 KeepInstances=0，别在两个分支里各抄一套校验。
$target = if ($Scenario -eq 'noWidgets') { 0 } elseif ($KeepInstances -ge 0) { $KeepInstances } else { -1 }
if ($target -ge 0) {
    if ($null -eq $widgetDoc) { throw ('沙盒里没有 widgets.json，' + $sandboxTag + ' 这一格会与对照组是同一份配置——不出这种数') }
    if ($instances -lt $target) { throw ('副本里只有 ' + $instances + ' 个实例，凑不出 ' + $target + ' 个——两格会是同一份配置，不出这种数') }
    $all = @($widgetDoc.Instances)
    # ⚠ 必须"先赋给变量、再直接赋属性"，不能写成 `$widgetDoc.Instances = if (...) { @(...) } else { @(...) }`：
    #   PowerShell 5.1 会把 if 表达式里**只有一颗**的数组在赋值时脱成单个对象，于是落盘的档变成
    #   "Instances": { … } 而不是 [ … ]，产品读到的是 JsonException ⇒ 组件配置损坏回退默认 ⇒ 恢复 0 颗，
    #   而我自己的"读回来数一遍"照样数得出 1 颗（坑表 #216 那一族：自己写的工具自己验＝空转。2026-10-01 02:15 踩过）。
    $keptList = @()
    if ($target -gt 0) { $keptList = @($all[0..($target - 1)]) }
    $widgetDoc.Instances = $keptList
    $widgetDoc | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $widgetsPath -Encoding UTF8
    $check = (Get-Content -LiteralPath $widgetsPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $kept = @($check.Instances)
    if ($kept.Count -ne $target) { throw ('沙盒里的实例数没改成 ' + $target + '（读回是 ' + $kept.Count + '），拒绝启动') }
    Write-Output ('保留 ' + $target + ' 颗（真档未动）：Kind=' + (($kept | ForEach-Object { $_.Kind }) -join ','))
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
        $session = $lines[$from..($lines.Count - 1)]
        # 场景自证必须由**产品自己**在日志里说，不能由我"写完再读回来数一遍"——后者连我写坏格式都验不出来。
        $corrupt = $session | Where-Object { $_ -match '组件配置损坏' } | Select-Object -First 1
        if ($corrupt) { Write-Output ('✗ 这一跑产品读组件档就失败（回退默认），实例数不是我要的那格——读数不采信：' + $corrupt) }
        $restored = $session | Where-Object { $_ -match '桌面组件恢复（(\d+) 个实例）' } | Select-Object -First 1
        if ($restored) {
            $n = [int][regex]::Match($restored, '桌面组件恢复（(\d+) 个实例）').Groups[1].Value
            $want = if ($target -ge 0) { [string]$target } else { '不削（副本原样）' }
            $verdict = if ($target -lt 0) { '（本格不削实例＝对照组，只记录不判定）' }
                        elseif ($n -ne $target) { '⇒ 不一致：这一跑不能当成 ' + $target + ' 颗的成本' }
                        else { '⇒ 与场景一致' }
            Write-Output ('产品自己恢复的实例数=' + $n + '（场景要求=' + $want + '）' + $verdict)
        } else {
            Write-Output '日志里没有"桌面组件恢复（N 个实例）"那一行——场景没法自证，读数不采信'
        }
        $session | Where-Object { $_ -match '\[内存\]|\[启动\]|\[耗时\]|自动备份|Ditto|二次启动' } | Select-Object -First 60 | ForEach-Object { Write-Output $_ }
    } else {
        Write-Output ('日志里没找到 pid=' + $pid2 + ' 的会话开始行——读数不采信，先查日志目录/滚动')
    }
}
Write-Output ('⚠ 这是一次被强杀的采样进程：日志里会有"有会话开始、无进程退出"，那是我的探针，不是他的崩溃（pid=' + $pid2 + '，时间窗 ' + $started.ToString('HH:mm:ss') + ' 起）')
