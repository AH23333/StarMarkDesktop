#Requires -Version 5.1
<#
.SYNOPSIS
    ServerGC A/B 的取证脚本（批次 R5）。给 §21 那条"Server GC 走 A/B 实测，不做默认承诺"提供可复跑的数字。

.DESCRIPTION
    量四件事，全部不需要管理员权限、不需要装任何东西：
      1) 工作集与私有内存的峰值/均值 —— Server GC 的代价就是这一维（铁律①：内存最小化）；
      2) 进程累计 CPU 秒的增量 —— 并发回收吃多核，这是"拿 CPU 换停顿"会显形的地方；
      3) 启动到主窗口出现的墙钟 —— 只有本脚本自己启动的进程才测这一条；
      4) 若这台机器装了 dotnet-counters，顺带采一份 System.Runtime 计数器的 CSV（GC 停顿与分配率的唯一正路）；
         没装就明确写"这一维测不到"，**不含糊、也不要求使用者先去装东西**——上面三件事照样能出结论。

    为什么"GC 停顿"不能自己算：.NET 9 进程不注册任何性能计数器类别（\.NET CLR Memory 是 Framework 时代的东西），
    直接读进程内存也推不出暂停时间。所以测不到就说测不到（坑表 #141：没有证据 ≠ 证据证明没有）。

    真机跑过的一条注意（2026-09-29 本脚本自测时踩到）：**"启动器交接"型的 exe 会秒退**。
    例子是 C:\Windows\System32\notepad.exe——那个进程 0.26 秒就结束，真正开窗的是 WindowsApps 里的另一个进程。
    本脚本会如实报"提前结束"（它测的确实是自己起的那个号），但**那一刻你测的不是应用**。
    所以换产物测量前先确认：Start-Process 起的号 == 你在任务栏看到的那个窗口所属的进程
    （对 StarMark 的 win-unpacked 产物成立；对任何"stub 转发"型启动器不成立）。

.PARAMETER ExePath
    要启动并测量的产物路径（A/B 时各跑一次，用 -Label 区分）。本脚本只结束**自己启动的**那个进程，
    已经在跑的实例（比如用户正在验收的那一个）绝不碰。

.PARAMETER ProcessName
    不启动、只附加测量：按进程名找（默认 StarMark）。有多个同名进程时取**最近启动的那个**，
    并把所有候选的 PID 与启动时间打出来——不许悄悄挑一个。

.PARAMETER Compare
    把两份已采好的 CSV 放在一起出对比表：-Compare a.csv,b.csv。这是 A/B 收口那一步，省掉手工比表格。

.PARAMETER SelfTest
    拿本脚本自己（powershell 进程）当被测对象跑一遍采样链路：验证读数、CSV、对比都成立，**不启动任何 GUI**。

.EXAMPLE
    powershell -NoProfile -File scripts\gc-ab-probe.ps1 -SelfTest -Seconds 3 -IntervalMs 200 -Label self

.EXAMPLE
    powershell -NoProfile -File scripts\gc-ab-probe.ps1 -ExePath D:\publish-A\StarMark.exe -Label A -Seconds 120

.EXAMPLE
    powershell -NoProfile -File scripts\gc-ab-probe.ps1 -Compare "$env:TEMP\StarMarkGcAb\gcab-A-1.csv","$env:TEMP\StarMarkGcAb\gcab-B-1.csv"
#>
[CmdletBinding()]
param(
    [string]$ExePath = '',
    [string]$ProcessName = 'StarMark',
    [string]$Label = 'run',
    [int]$Seconds = 60,
    [int]$IntervalMs = 500,
    [string]$OutDir = '',
    [string]$Compare = '',
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Write-Line {
    # 必须声明 ValueFromPipeline：不加的话 `... | Write-Line` 会**安静地什么都不输出**
    # （PowerShell 不会把管道输入绑到普通函数的位置上），A/B 对比表就是这么整个消失的。
    param([Parameter(ValueFromPipeline)]$text)
    process { Write-Host $text }
}

function Get-Sample($proc) {
    # 一次读数。Refresh() 之后才拿得到新的 CPU/内存值；这些属性在进程正在退出时会逐个抛，
    # 所以每一项单独兜住——**采证工具不能因为"目标刚好在这一刻结束"就整个崩掉**。
    try { $proc.Refresh() } catch { return $null }
    $cpu = 0.0
    try { $cpu = $proc.TotalProcessorTime.TotalSeconds } catch { }
    $threads = 0
    try { $threads = $proc.Threads.Count } catch { }
    $handles = 0
    try { $handles = $proc.HandleCount } catch { }
    [pscustomobject]@{
        At         = (Get-Date).ToString('HH:mm:ss.fff')
        WorkingSet = [long]$proc.WorkingSet64
        PrivateMem = [long]$proc.PrivateMemorySize64
        CpuSeconds = [math]::Round($cpu, 3)
        Threads    = $threads
        HandleCount = $handles
    }
}

function Format-Mb($bytes) { return [math]::Round($bytes / 1MB, 1) }

# ───────────────────────── 对比模式：不做任何采样 ─────────────────────────
if ($Compare -ne '') {
    $files = @($Compare -split ';' | Where-Object { $_ -ne '' })
    if ($files.Count -ne 2) {
        Write-Line '[用法] -Compare 需要两份 CSV，用分号隔开：-Compare a.csv;b.csv'
        exit 2
    }
    $missing = @($files | Where-Object { -not (Test-Path $_) })
    if ($missing.Count -gt 0) {
        Write-Line ('[读不到] 这些 CSV 不存在：' + ($missing -join '，') + '（CSV 默认落在 %TEMP%\StarMarkGcAb\）')
        exit 2
    }
    Write-Line '=== A/B 对比（同一把尺子：工作集/私有内存峰值、CPU 增量）==='
    $rows = @()
    foreach ($f in $files) {
        $data = @(Import-Csv $f)
        if ($data.Count -eq 0) { Write-Line ('[空档] ' + $f + ' 一个样本都没有'); exit 2 }
        $ws = @($data | ForEach-Object { [long]$_.WorkingSet })
        $pm = @($data | ForEach-Object { [long]$_.PrivateMem })
        $cpuFirst = [double]$data[0].CpuSeconds
        $cpuLast = [double]$data[$data.Count - 1].CpuSeconds
        $rows += [pscustomobject]@{
            File         = (Split-Path $f -Leaf)
            Samples      = $data.Count
            WsPeakMB     = Format-Mb ($ws | Measure-Object -Maximum).Maximum
            WsAvgMB      = Format-Mb ($ws | Measure-Object -Average).Average
            PrivatePeakMB = Format-Mb ($pm | Measure-Object -Maximum).Maximum
            CpuDeltaSec  = [math]::Round($cpuLast - $cpuFirst, 2)
        }
    }
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Line
    Write-Line '判读：Server GC 的赌注是"多花内存与 CPU 换更短的暂停"。两列差异小于 5% 时，'
    Write-Line '这一维就分不出高下——那就按 §21 的原口径走"内存最小化"，不开 ServerGC。'
    exit 0
}

# ───────────────────────── 参数消毒：坏值回默认，并说原因 ─────────────────────────
if ($Seconds -lt 1 -or $Seconds -gt 3600) {
    Write-Line ('[坏值] -Seconds 只接受 1–3600，给的是 ' + $Seconds + '，按 60 跑（回默认，不夹到边上）。'); $Seconds = 60
}
if ($IntervalMs -lt 100 -or $IntervalMs -gt 10000) {
    Write-Line ('[坏值] -IntervalMs 只接受 100–10000，给的是 ' + $IntervalMs + '，按 500 跑。'); $IntervalMs = 500
}
if ($OutDir -eq '') { $OutDir = Join-Path $env:TEMP 'StarMarkGcAb' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$csvPath = Join-Path $OutDir ('gcab-{0}-{1}.csv' -f $Label, $stamp)

# ───────────────────────── 找/起目标进程 ─────────────────────────
$proc = $null
$owned = $false
$ownedStart = $null
$readySeconds = $null

if ($SelfTest) {
    $proc = Get-Process -Id $PID
    Write-Line ('目标：本脚本自己（PID ' + $PID + '，' + $proc.ProcessName + '）——SelfTest 不启动任何 GUI。')
}
elseif ($ExePath -ne '') {
    if (-not (Test-Path $ExePath)) {
        Write-Line ('[读不到] 这个产物路径不存在：' + $ExePath)
        Write-Line '  A/B 两侧都要先有发布产物；本脚本只测量，不替你构建。'
        exit 3
    }
    $full = (Resolve-Path $ExePath).Path
    Write-Line ('启动产物：' + $full)
    $proc = Start-Process -FilePath $full -PassThru
    $owned = $true
    $ownedStart = $proc.StartTime
    # 启动→主窗口出现：轮询到 60 秒为止。窗口句柄出现不等于渲染完成，这一条只当"进程活着且建了窗"的下界。
    $deadline = (Get-Date).AddSeconds(60)
    $t0 = Get-Date
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) { $readySeconds = [math]::Round(((Get-Date) - $t0).TotalSeconds, 2); break }
        if ($proc.HasExited) { break }
    }
    if ($proc.HasExited) {
        # 起不来又立刻退出的两种常见原因：框架依赖产物缺运行时、或路径指向的不是可执行文件。
        # 这句话必须报"实际等了多久"，不能照抄 60 秒——那时等待循环早就提前 break 了。
        Write-Line ('[提前结束] 进程在 {0} 秒就退出了，主窗口这一维记「没测到」。若是框架依赖产物，先确认这台机器有对应运行时。' -f [math]::Round(((Get-Date) - $t0).TotalSeconds, 2))
    } elseif ($null -eq $readySeconds) {
        Write-Line '[没等到] 60 秒内没出现主窗口句柄——这一维记为"没测到"，不影响内存/CPU 两维继续采。'
    } else {
        Write-Line ('主窗口句柄出现在 ' + $readySeconds + ' 秒后（这一条是"进程活着且建了窗"的下界，不是渲染完成）')
    }
}
else {
    $cands = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($cands.Count -eq 0) {
        Write-Line ('[找不到] 现在没有在跑的 ' + $ProcessName + ' 进程。')
        Write-Line '  要么先启动程序，要么改用 -ExePath 让本脚本自己起（自己起的才会自己收）。'
        exit 3
    }
    if ($cands.Count -gt 1) {
        Write-Line ('[多个同名进程] 共 ' + $cands.Count + ' 个，取最近启动的那个；候选清单：')
        foreach ($c in $cands) { Write-Line ('   PID ' + $c.Id + ' 启动于 ' + $c.StartTime.ToString('MM-dd HH:mm:ss')) }
    }
    $proc = ($cands | Sort-Object StartTime -Descending | Select-Object -First 1)
    Write-Line ('只读附加：PID ' + $proc.Id + '（本脚本不会结束一个不是自己启动的进程）')
}

# ───────────────────────── dotnet-counters：有就用，没有就明说 ─────────────────────────
$counterJob = $null
$counterCsv = Join-Path $OutDir ('counters-{0}-{1}.csv' -f $Label, $stamp)
$countersCmd = Get-Command 'dotnet-counters' -ErrorAction SilentlyContinue
if ($null -ne $countersCmd) {
    Write-Line ('顺带采 GC 计数器：' + $counterCsv)
    $counterJob = Start-Process -FilePath $countersCmd.Source `
        -ArgumentList @('monitor', '--process-id', "$($proc.Id)", '--refresh-interval', '1',
                       '--format', 'csv', '-o', $counterCsv, '--', 'System.Runtime') `
        -PassThru -NoNewWindow -ErrorAction SilentlyContinue
} else {
    Write-Line '[测不到] 这台机器没有 dotnet-counters → GC 停顿（collections / time in GC）与分配率这一维采不到。'
    Write-Line '         上面三件事（工作集 / 私有内存 / CPU）不受影响，结论照样能出。'
    Write-Line '         这一维自动补上即可：装好 dotnet-counters（dotnet tool install --global dotnet-counters）后不用改本脚本，下次跑就带着采。'
}

# ───────────────────────── 采样 ─────────────────────────
Write-Line ('采样 ' + $Seconds + ' 秒，每 ' + $IntervalMs + ' 毫秒一次 → ' + $csvPath)
$samples = @()
$endAt = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $endAt) {
    if ($proc.HasExited) { Write-Line '[中途退出] 目标进程提前结束了，样本只到这一刻。'; break }
    $s = Get-Sample $proc
    if ($null -ne $s) {
        $s | Add-Member -NotePropertyName Label -NotePropertyValue $Label -Force
        $s | Add-Member -NotePropertyName Pid -NotePropertyValue $proc.Id -Force
        $samples += $s
    }
    Start-Sleep -Milliseconds $IntervalMs
}

if ($null -ne $counterJob -and -not $counterJob.HasExited) {
    try { $counterJob.Kill() } catch { }
}
if ($samples.Count -eq 0) {
    Write-Line '[没有样本] 一次都没读到——通常是目标进程启动失败或立刻退出。'
    exit 4
}
$samples | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $csvPath

$ws = @($samples | ForEach-Object { $_.WorkingSet })
$pm = @($samples | ForEach-Object { $_.PrivateMem })
$cpuFirst = $samples[0].CpuSeconds
$cpuLast = $samples[$samples.Count - 1].CpuSeconds

Write-Line ''
Write-Line ('=== 小结（' + $Label + '，样本 ' + $samples.Count + ' 个）===')
Write-Line ('工作集：峰值 {0} MB / 均值 {1} MB' -f (Format-Mb ($ws | Measure-Object -Maximum).Maximum), (Format-Mb ($ws | Measure-Object -Average).Average))
Write-Line ('私有内存：峰值 {0} MB' -f (Format-Mb ($pm | Measure-Object -Maximum).Maximum))
Write-Line ('CPU 累计：{0} → {1} 秒（窗口内增量 {2} 秒，线程峰值 {3}）' -f $cpuFirst, $cpuLast, [math]::Round($cpuLast - $cpuFirst, 2), ($samples | ForEach-Object { $_.Threads } | Measure-Object -Maximum).Maximum)
if ($null -ne $readySeconds) { Write-Line ('启动到主窗口句柄：' + $readySeconds + ' 秒') }
Write-Line ('CSV：' + $csvPath)
Write-Line ('两份都采完之后对比：powershell -NoProfile -File scripts\gc-ab-probe.ps1 -Compare a.csv;b.csv')

# ───────────────────────── 收尾：只收自己起的那个 ─────────────────────────
if ($owned) {
    try {
        $alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if ($null -ne $alive) {
            # 二次确认启动时间没变：PID 会被复用，句柄过期就会误杀别人的同号进程。
            if ($alive.StartTime -eq $ownedStart) {
                $alive.Kill()
                Write-Line ('已收回自己启动的进程（PID ' + $proc.Id + '）。')
            } else {
                Write-Line ('[没动它] PID ' + $proc.Id + ' 的启动时间对不上，说明这个号已经换人了——绝不结束。')
            }
        }
    } catch {
        Write-Line ('[没关掉] ' + $proc.Id + '：' + $_.Exception.Message + '（本脚本不会要求你去任务管理器动手，这条只报告事实）')
    }
}
