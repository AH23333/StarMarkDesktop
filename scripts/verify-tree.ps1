# 冻结清单（starmark-tree.json）的唯一读法（P-209）：树↔清单逐颗对（sha256＋大小＋颗数＋版本），
# 给了 -ZipPath 再把 zip 的条目逐颗对上去。同一把尺三颗调用点共用（#189/#193：两处实现必漂移）：
#   publish-tree 产完树读回自证、release-package 打完 zip 三方对账、make-installer 编译之前再对一次。
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts\verify-tree.ps1 -TreeDir <树的绝对路径> [-ExpectVersion x.y.z] [-ZipPath <zip>]
#
# 纪律：每一句比较都同行 throw（只查不拒＝那句比较是装饰）；这颗脚本只读不写——
# 它若动过任何一颗字节，"读回自证"就成了"自己验自己刚写的东西"。
# 路径口径与更新链同一规范形（正斜杠，UpdateAssets.NormalizeEntryName）：清单里的路径
# 与 update-manifest.json 的 files 那一份长得一样，两份清单对起来不用换算。
param(
    [Parameter(Mandatory = $true)][string]$TreeDir,
    [string]$ExpectVersion,
    [string]$ZipPath
)

$ErrorActionPreference = "Stop"
$TreeDir = (Resolve-Path -LiteralPath $TreeDir).Path
$manifestName = "starmark-tree.json"
$manifestPath = Join-Path $TreeDir $manifestName
if (-not [System.IO.File]::Exists($manifestPath)) { throw "树里没有冻结清单 $manifestPath——这棵树不是 publish-tree 产的（P-209：先跑 scripts\publish-tree.ps1 产一棵带清单的树）" }
$manifest = ConvertFrom-Json (Get-Content -Raw -LiteralPath $manifestPath)
if ($ExpectVersion) {
    if (([string]$manifest.version) -ne $ExpectVersion) { throw "清单记的版本是 $($manifest.version)，这一步要出的是 $ExpectVersion——树与这一版对不上" }
}
$listed = @($manifest.files)
$byPath = @{}
foreach ($f in $listed) {
    $p = [string]$f.path
    if ($byPath.ContainsKey($p)) { throw "清单里 $p 记了两次" }
    $byPath[$p] = $f
}

# 树那一侧：逐颗取相对路径（归一成清单那种正斜杠写法），清单自身不算树的内容。
$actual = [string[]]@()
foreach ($one in (Get-ChildItem -LiteralPath $TreeDir -Recurse -File)) {
    $rel = $one.FullName.Substring($TreeDir.Length + 1).Replace("\", "/")
    if (-not ($rel.Equals($manifestName))) { $actual += $rel }
}
if ($actual.Count -ne $listed.Count) { throw "树里量到 $($actual.Count) 颗文件，清单记了 $($listed.Count) 颗——多一颗少一颗都算对不上" }
foreach ($rel in $actual) {
    $entry = $byPath[$rel]
    if (-not $entry) { throw "树里有清单没记的文件：$rel" }
    $full = Join-Path $TreeDir $rel
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne ([string]$entry.sha256).ToLowerInvariant()) { throw "$rel 的字节与清单记的哈希对不上（树里这份 sha256 $hash）——树在清单冻结之后被人动过" }
    if ((Get-Item -LiteralPath $full).Length -ne [long]$entry.size) { throw "$rel 的大小与清单记的对不上——树在清单冻结之后被人动过" }
}

# zip 那一侧（给了 -ZipPath 才对）：条目名归一成同一规范形，逐颗流式算 sha256 对清单；
# 内嵌那份清单与树上那份必须逐字节相同——zip 与树从两条路走，走到这里的必须是同一批字节。
if ($ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $treeManifestHash = ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($manifestPath)))).Replace("-", "").ToLowerInvariant()
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath).Path)
    $inZip = 0
    $sawManifest = $false
    try {
        foreach ($z in $zip.Entries) {
            $name = $z.FullName.Replace("\", "/").TrimStart("/")
            if ($name.EndsWith("/")) { continue }
            $stream = $z.Open()
            try {
                $digest = ([System.BitConverter]::ToString($sha.ComputeHash($stream))).Replace("-", "").ToLowerInvariant()
            } finally {
                $stream.Dispose()
            }
            if ($name.Equals($manifestName)) {
                if ($digest -ne $treeManifestHash) { throw "zip 里那份清单与树上那份字节不同——两条路装出去的树不是同一批字节" }
                $sawManifest = $true
                continue
            }
            $m = $byPath[$name]
            if (-not $m) { throw "zip 里有清单没记的条目：$name" }
            if ($digest -ne ([string]$m.sha256).ToLowerInvariant()) { throw "zip 里 $name 的字节与清单对不上——zip 打包前后树被人动过" }
            if ($z.Length -ne [long]$m.size) { throw "zip 里 $name 的大小与清单对不上" }
            $inZip += 1
        }
        if (-not $sawManifest) { throw "zip 里没有内嵌清单 $manifestName——打的不是那棵带清单的树" }
        if ($inZip -ne $listed.Count) { throw "zip 里有 $($inZip) 颗载荷条目，清单记了 $($listed.Count) 颗" }
    } finally {
        $zip.Dispose()
        $sha.Dispose()
    }
}
Write-Host "树与冻结清单逐颗对上：$($actual.Count) 颗文件、版本 $($manifest.version)、commit $($manifest.commit)$(if ($ZipPath) { "；zip 的条目也对上" })"
