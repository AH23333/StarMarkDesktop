#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using static StarMark.Tests.SourceGate;

namespace StarMark.Tests;

/// <summary>
/// 那颗星（批次 VC-2）的闸门：<b>资产本身的形状</b>与<b>各处接线读的是不是同一份</b>。
/// <para><b>为什么这一族不能只靠肉眼</b>：图标是二进制资产，构建与运行都不校验它。
/// 这一批我亲手踩过三处——极坐标线性插值把星边画鼓（看着像六瓣花）、<c>%(π/5)</c> 画出十芒、
/// <c>+π/2</c> 让尖朝下；三处的表现都是"编译过、测试全绿、图标不好看"。
/// 现在把它们钉成可机检的形状判据（尖朝上＝每 72° 一只尖、每 36° 处必须是谷，且谷不超过尖的七成半），
/// 换一个人重画也躲不过。</para>
/// <para><b>另一族是出处</b>：exe 图标（csproj 的 <c>ApplicationIcon</c>）、随包两份 <c>Content</c>、
/// 窗口（<c>SetIcon</c>）、托盘（<c>LoadImageW</c>）、安装包（<c>SetupIconFile</c>）——各写一条路径就会漂成
/// "标题栏有星、任务栏是通用图"（#123 那一族：一处事实两个出处）。
/// 所以这里的几何读数一律<b>从生成器 <c>scripts/make-app-icon.mjs</c> 里读</b>，闸门不再抄第二份常量。</para>
/// </summary>
public sealed class AppIconGateTests
{
    private const string Generator = "scripts/make-app-icon.mjs";
    private const string TileAsset = "assets/app/StarMark.ico";
    private const string TrayAsset = "assets/app/StarMarkTray.ico";
    private const string UiProject = "src/StarMark.UI/StarMark.UI.csproj";
    private const string MainWindow = "src/StarMark.UI/MainWindow.xaml.cs";
    private const string TrayHost = "src/StarMark.Integrations/SystemTray/TrayHost.cs";
    private const string Iss = "scripts/installer/StarMark.iss";
    private const string PublishTree = "scripts/publish-tree.ps1";

    /// <summary>生成器里那份唯一的星形金色——闸门读它，不重抄（重抄就是第二个出处）。</summary>
    private static (int R, int G, int B) GeneratorStarRgb()
    {
        var m = Regex.Match(ReadRepoFile(Generator), @"const STAR_RGB = \[(0x[0-9a-fA-F]+), (0x[0-9a-fA-F]+), (0x[0-9a-fA-F]+)\]");
        Assert.True(m.Success, "生成器里没找到 STAR_RGB 那一行（几何的出处搬了家，闸门要先跟上）");
        return (Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16), Convert.ToInt32(m.Groups[3].Value, 16));
    }

    /// <summary>生成器列出的档位梯子，同样只读不抄。</summary>
    private static int[] GeneratorLadder()
    {
        var m = Regex.Match(ReadRepoFile(Generator), @"const SIZES = \[([^\]]+)\]");
        Assert.True(m.Success, "生成器里没找到 SIZES 那一行");
        return m.Groups[1].Value.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
    }

    // ===== 只读这点像素：ICO 容器 ＋ 里面那份 PNG =====

    private readonly record struct IcoEntry(int Size, byte WidthByte, byte HeightByte, int Planes, int BitCount, bool IsPng, byte[] Blob);

    private static List<IcoEntry> ReadIco(string relativePath)
    {
        var data = File.ReadAllBytes(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.True(data.Length > 6, $"{relativePath} 连 6 字节的头都放不下");
        Assert.Equal((ushort)0, BitConverter.ToUInt16(data, 0));        // 保留字段
        Assert.Equal((ushort)1, BitConverter.ToUInt16(data, 2));        // 类型 1＝图标（不是光标、不是位图）
        var count = BitConverter.ToUInt16(data, 4);
        var list = new List<IcoEntry>();
        for (var i = 0; i < count; i++)
        {
            var e = 6 + 16 * i;
            var byteW = data[e];
            var byteH = data[e + 1];
            var size = (int)BitConverter.ToUInt32(data, e + 8);
            var offset = (int)BitConverter.ToUInt32(data, e + 12);
            Assert.True(offset + size <= data.Length, $"{relativePath} 第 {i} 档指到文件外面");
            var blob = data[offset..(offset + size)];
            Assert.True(blob.Length > 8 && blob[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
                $"{relativePath} 第 {i} 档不是 PNG（这一族只用 PNG-in-ICO，退回 BMP 会让 alpha 走另一条路）");
            Assert.Equal(byteH, byteW);
            // 256 在这一族的历史里写成 0，其余写成自己；把 256 写成 16 会被 shell 当成 16px 用。
            list.Add(new IcoEntry(byteW == 0 ? 256 : byteW, byteW, byteH,
                BitConverter.ToUInt16(data, e + 4), BitConverter.ToUInt16(data, e + 6), true, blob));
        }
        return list;
    }

    /// <summary>
    /// PNG → RGBA。<b>这一颗刻意不复用 <c>ClipboardPayload.TryDecodePng</c></b>：那把尺子按 §2 的裁决
    /// 把 alpha 一律归成 255（生产里要防的是"两处解码算出两个哈希"），而托盘那一份的形状<b>全部住在 alpha 里</b>
    /// （它的 RGB 处处是同一组金色），丢了 alpha 就等于什么都没验。
    /// 这里读的是本仓生成器产出的、已知形状的资产（非隔行、每行滤波器字节为 0），不是用户字节，
    /// 所以它只负责"照自己的规格解得开，解不开就红"，<b>不许反过来搬进生产路径</b>。
    /// </summary>
    private static (int Width, int Height, byte[] Rgba) DecodeRgba(byte[] png)
    {
        var pos = 8;
        int width = 0, height = 0;
        byte depth = 0, colorType = 0, interlace = 1;
        var idat = new List<byte[]>();
        while (pos + 12 <= png.Length)
        {
            var len = (int)Be32(png, pos);
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var at = pos + 8;
            if (type == "IHDR")
            {
                width = (int)Be32(png, at);
                height = (int)Be32(png, at + 4);
                depth = png[at + 8];
                colorType = png[at + 9];
                interlace = png[at + 12];
            }
            else if (type == "IDAT") idat.Add(png[at..(at + len)]);
            else if (type == "IEND") break;
            pos = at + len + 4;
        }
        Assert.True(depth == 8 && colorType == 6 && interlace == 0,
            $"只按\"8 位／RGBA／非隔行\"读自产资产（读到 depth={depth} colorType={colorType} interlace={interlace}）");
        using var raw = new ZLibStream(new MemoryStream(idat.SelectMany(x => x).ToArray()), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        raw.CopyTo(ms);
        var bytes = ms.ToArray();
        var stride = width * 4;
        Assert.Equal(height * (1 + stride), bytes.Length);
        var rgba = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            // 生成器每行都写滤波器 0（None）。真用上了过滤器就不是"逐行剥一个字节"能解的，这里当场红而不是猜。
            Assert.Equal((byte)0, bytes[y * (1 + stride)]);
            bytes.AsSpan(y * (1 + stride) + 1, stride).CopyTo(rgba.AsSpan(y * stride));
        }
        return (width, height, rgba);
    }

    private static uint Be32(byte[] b, int at) => (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

    /// <summary>一帧的取像素器（R,G,B,A）。越界读成"全透明"，所以探针不会越画越远。</summary>
    private static Func<int, int, (int R, int G, int B, int A)> Frame(int width, int height, byte[] rgba)
        => (x, y) =>
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return (0, 0, 0, 0);
            var i = (y * width + x) * 4;
            return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
        };

    /// <summary>
    /// "这一格是星"＝<b>半透明以上、且红压过蓝</b>。金色是 (252,182,48)（R≫B），圆角底是蓝紫渐变（B≫R），
    /// 一次比较就能把星从底里分出来；托盘那一帧 RGB 处处是金色，靠的是 alpha 分形状。
    /// 两个条件缺一个就会把另一份读成"整幅都是星"。
    /// </summary>
    private static bool IsStar((int R, int G, int B, int A) px) => px.A > 128 && px.R > px.B;

    /// <summary>沿某个方向从中心往外走，第一次离开星时的那个半径。</summary>
    private static double ProbeRadius(Func<int, int, (int R, int G, int B, int A)> px, double cx, double cy, double angleDeg, double max)
    {
        var rad = angleDeg * Math.PI / 180.0;
        for (var r = 0.0; r <= max; r += 0.25)
        {
            if (!IsStar(px((int)Math.Round(cx + r * Math.Cos(rad)), (int)Math.Round(cy + r * Math.Sin(rad))))) return r;
        }
        return max;
    }

    /// <summary>星的质心。中心是生成器算出来的（还往下挪了一点做光学居中），闸门不去复算那个偏移，只取质心。</summary>
    private static (double Cx, double Cy) StarCentroid(Func<int, int, (int R, int G, int B, int A)> px, int size)
    {
        double sx = 0, sy = 0;
        var n = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!IsStar(px(x, y))) continue;
                sx += x;
                sy += y;
                n++;
            }
        }
        Assert.True(n > size, $"{size}px 那一档只认得出 {n} 格星——星没画上去（或小到认不出）");
        return (sx / n, sy / n);
    }

    // ===== 资产本身 =====

    /// <summary>
    /// 两份 .ico 都是 <b>PNG-in-ICO</b>，档位与生成器列的那份梯子<b>逐档相等</b>。
    /// <para>"补一档／少一档"不会让任何东西变红：shell 只会拿不到合适的那一档，16px 上糊成一团，
    /// 而 256 档一旦缺失，放大查看与磁贴就退回拉伸。梯子只有一份出处，所以这里对着生成器比，
    /// 不比一串手抄的数字。</para>
    /// </summary>
    [Fact]
    public void BothAssetsArePngInIcoCarryingExactlyTheSizesTheGeneratorLists()
    {
        var ladder = GeneratorLadder();
        foreach (var asset in new[] { TileAsset, TrayAsset })
        {
            var entries = ReadIco(asset);
            Assert.Equal(ladder, entries.Select(e => e.Size).ToArray());
            Assert.All(entries, e =>
            {
                Assert.Equal(1, e.Planes);
                Assert.Equal(32, e.BitCount);
                Assert.Equal(e.Size == 256 ? (byte)0 : (byte)e.Size, e.WidthByte);
            });
        }
    }

    /// <summary>
    /// 星是<b>五角、尖朝上</b>，谷真的收进去；两份资产、两档尺寸（最小档与最大档）各验一次。
    /// <para>三条判据各自咬住我这次真犯过的一个错：尖朝上（<c>+π/2</c> 会把尖翻到下面）、
    /// 每 72° 一只尖（<c>%(π/5)</c> 画出的是十芒）、谷真的深（极坐标线性插值把边画鼓，谷就不深了）。
    /// 判据一律写成<b>比较</b>（谷半径 ≤ 尖半径的七成半；现场读数：五角时每档最差 0.61，十芒会顶到 1.0），
    /// 不钉像素计数——采样密度一调计数就变，
    /// 那是等价改写，不是事故（记忆 ⑤ 讲的"过钉"）。</para>
    /// </summary>
    [Fact]
    public void TheMarkIsAFivePointedStarWithItsTipUpInEveryFrameProbed()
    {
        foreach (var asset in new[] { TileAsset, TrayAsset })
        {
            foreach (var size in new[] { 16, 256 })
            {
                var (w, h, rgba) = DecodeRgba(ReadIco(asset).Single(e => e.Size == size).Blob);
                Assert.Equal(size, w);
                Assert.Equal(size, h);
                var px = Frame(w, h, rgba);
                var (cx, cy) = StarCentroid(px, size);
                Assert.True(Math.Abs(cx - size / 2.0) < size * 0.05,
                    $"{asset}@{size}：左右不对称（质心 x={cx:F1}），星歪了");

                var tips = Enumerable.Range(0, 5).Select(k => ProbeRadius(px, cx, cy, -90 + 72 * k, size / 2.0)).ToArray();
                var valleys = Enumerable.Range(0, 5).Select(k => ProbeRadius(px, cx, cy, -54 + 72 * k, size / 2.0)).ToArray();

                Assert.All(tips, t => Assert.True(t >= size * 0.30, $"{asset}@{size}：有一只尖没撑开（半径 {t:F1}）——尖朝下时正上方读到的是谷，红在这一条"));
                for (var k = 0; k < 5; k++)
                {
                    // 尖必须是尖、谷必须是谷：十芒会在"谷"的方向上也顶出去（现场读数：五角时谷/尖最差 0.61，十芒≈1.0）。
                    Assert.True(valleys[k] <= tips[k] * 0.75,
                        $"{asset}@{size}：第 {k} 对尖谷分不开（尖 {tips[k]:F1}／谷 {valleys[k]:F1}）——十芒或把边画鼓都会红在这里");
                }
                Assert.True(tips.Max() - tips.Min() <= tips.Max() * 0.25,
                    $"{asset}@{size}：五只尖长短不一（{string.Join("/", tips.Select(t => t.ToString("F1")))}）");
            }
        }
    }

    /// <summary>
    /// 小档的粗细补偿<b>真的生效</b>：16px 那一档的星占画面的比例，必须明显大于 256px 那一档。
    /// <para>生成器注释写的是"16px 上标准五角星的角会糊成一坨"（批次 WT 同一招：画得出＝认得出），
    /// 做法是小档把外接半径放大、内径收胖。<b>这里比的是两档之间的比例，不是某一档的像素数</b>：
    /// 现场读数是 16px 占 25.8%、256px 占 16.2%（1.6 倍），补偿一旦被删掉就掉回 1.0 倍，
    /// 而采样密度怎么调都只是在 1.6 附近晃。直接钉"16px 的谷/尖比"我试过——小档的抗锯齿噪声比大档大，
    /// 那种写法会把"没补偿"与"采样换了"混在一起，判不准。</para>
    /// </summary>
    [Fact]
    public void TheSmallSizeFrameCoversClearlyMoreOfTheCanvasThanTheLargeSizeOne()
    {
        double Coverage(string asset, int size)
        {
            var (w, _, rgba) = DecodeRgba(ReadIco(asset).Single(e => e.Size == size).Blob);
            var px = Frame(w, w, rgba);
            var hit = Enumerable.Range(0, w * w).Count(i => IsStar(px(i % w, i / w)));
            return (double)hit / (w * w);
        }
        foreach (var asset in new[] { TileAsset, TrayAsset })
        {
            var small = Coverage(asset, 16);
            var large = Coverage(asset, 256);
            Assert.True(small >= large * 1.25,
                $"{asset}：16px 的覆盖率（{small:P1}）没有明显高过 256px（{large:P1}）——小档粗细补偿没了，托盘那颗星会糊成一坨");
        }
    }

    /// <summary>
    /// 金色就是生成器声明的那一组；两份资产的分工是<b>带底</b>与<b>透明底只留星</b>。
    /// <para>托盘为什么不能带底：16px 上一整块蓝紫只会糊成一团，认不出标记。这一条同时钉住
    /// "别把两份生成成同一颗"：带底那份在圆角外什么都不画，不带底那份<b>不透明的地方每一格都是那组金</b>。</para>
    /// </summary>
    [Fact]
    public void TheTiledMarkCarriesTheGradientBaseAndTheTrayMarkIsTheStarAlone()
    {
        var (sr, sg, sb) = GeneratorStarRgb();
        var tile = DecodeRgba(ReadIco(TileAsset).Single(e => e.Size == 256).Blob);
        var tray = DecodeRgba(ReadIco(TrayAsset).Single(e => e.Size == 256).Blob);
        var tilePx = Frame(tile.Width, tile.Height, tile.Rgba);
        var trayPx = Frame(tray.Width, tray.Height, tray.Rgba);

        // 星的正中那一格必须是原色（不是混出来的近似值）：金色被改、中心被别的东西盖住，都红在这里。
        var (cx, cy) = StarCentroid(tilePx, tile.Width);
        var center = tilePx((int)Math.Round(cx), (int)Math.Round(cy));
        Assert.Equal((sr, sg, sb, 255), (center.R, center.G, center.B, center.A));

        // 带底那份：画面正中靠左那一条是渐变蓝（B 压过 R、且不透明），而圆角外什么都不画。
        var baseLeft = tilePx(0, tile.Height / 2);
        Assert.True(baseLeft.B > baseLeft.R && baseLeft.A == 255, $"带底那份的蓝紫底不见了（读到 ({baseLeft.R},{baseLeft.G},{baseLeft.B},{baseLeft.A})）");
        var corner = tilePx(0, 0);
        Assert.True(corner.A == 0 && corner.R == 0 && corner.G == 0 && corner.B == 0,
            $"圆角外不该有东西，读到 ({corner.R},{corner.G},{corner.B},{corner.A})");

        // 透明底那份：凡不透明的地方每一格都是那组金；星之外一律全透明。
        var opaque = 0;
        for (var i = 0; i < tray.Width * tray.Height; i++)
        {
            var p = trayPx(i % tray.Width, i / tray.Width);
            if (p.A <= 128) continue;
            opaque++;
            Assert.True(p.R == sr && p.G == sg && p.B == sb,
                $"托盘那份第 {i} 格不透明却不是金色本体（读到 ({p.R},{p.G},{p.B})）——托盘里混进了底");
        }
        Assert.True(opaque > 0, "托盘那份整幅透明＝什么图标都没有");
        Assert.True(trayPx(0, tray.Height / 2).A == 0, "托盘那份在星之外还有不透明的东西（\"透明底只留一颗星\"这条分工不能丢）");
    }

    // ===== 接线：各处读的必须是同一份 =====

    /// <summary>
    /// <c>csproj</c>：<b>exe 图标</b>与<b>随包两份</b>都指 <c>assets/app</c> 那一份，且那份真在盘上。
    /// <para><c>ApplicationIcon</c> 决定资源管理器／快捷方式／卸载面板里那颗；少了它就是
    /// "标题栏有星、任务栏通用图"那种对不上账的样子。两份 <c>Content</c> 决定运行时读不读得到——
    /// 靠 <c>Link</c> 扁平进输出目录，<c>MainWindow</c> 与 <c>TrayHost</c> 才敢按文件名读。</para>
    /// </summary>
    [Fact]
    public void TheUiProjectPointsItsExeIconAndItsPackagedContentAtThatOneAssetFolder()
    {
        var csproj = ReadRepoFile(UiProject);
        var appIcon = Regex.Match(csproj, @"<ApplicationIcon>([^<]+)</ApplicationIcon>");
        Assert.True(appIcon.Success, "csproj 里没有 <ApplicationIcon>（exe／快捷方式／卸载面板会退回通用图）");
        Assert.Equal(1, Count(csproj, "<ApplicationIcon>"));
        Assert.True(appIcon.Groups[1].Value.Trim() == @"..\..\assets\app\StarMark.ico",
            $"ApplicationIcon 指的不再是那份资产（读到 {appIcon.Groups[1].Value.Trim()}）");
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "assets", "app", "StarMark.ico")), "ApplicationIcon 指的那份资产不在仓里");

        // 那份资产目录只许出现两次：一次给 exe，一次给随包 Content。第三次＝第二条路径，迟早漂。
        Assert.Equal(2, Count(csproj, @"..\..\assets\app\StarMark.ico"));
        Assert.Equal(1, Count(csproj, @"..\..\assets\app\StarMarkTray.ico"));
        Assert.Equal(2, Count(csproj, @"CopyToOutputDirectory=""PreserveNewest"""));
    }

    /// <summary>
    /// 窗口与托盘<b>从随包那份文件读</b>，并且<b>运行时不再自己画一颗星</b>。
    /// <para>托盘原先在 <c>TrayHost</c> 里用 <c>CreateDIBSection</c> 逐像素画一颗五角星，扩展那边又有一份
    /// <c>gen-icons.mjs</c>：两处各画各的，形状迟早分岔。现在几何只在生成器一处，运行时只按
    /// <c>SM_CXSMICON</c> 取一档。禁项扫的是<b>整个 src</b>而不是那一个文件——方法搬去第三个文件而禁项
    /// 只钉一处，下一次它就会假绿（记忆 ⑦）。</para>
    /// </summary>
    [Fact]
    public void TheWindowAndTrayReadThePackagedFileAndNoSourceDrawsAStarAnymore()
    {
        var window = Code(ReadRepoPartials(MainWindow));
        Assert.Equal(1, Count(window, "SetIcon("));
        Assert.True(window.Contains(@"SetIcon(Path.Combine(AppContext.BaseDirectory, ""StarMark.ico""))", StringComparison.Ordinal),
            "窗口那颗星不再从随包文件按文件名读（BaseDirectory 之外的路径都算第二条出处）");

        var tray = Code(ReadRepoFile(TrayHost));
        Assert.True(tray.Contains(@"AppContext.BaseDirectory, ""StarMarkTray.ico""", StringComparison.Ordinal),
            "托盘那颗星不再从随包文件读");
        Assert.Contains("GetSystemMetrics(SM_CXSMICON)", tray, StringComparison.Ordinal);
        Assert.Contains("LoadImageW(", tray, StringComparison.Ordinal);
        // 取不到图标时宁可少交一颗 NIF_ICON，也不交一颗空白句柄过去——这条行为不许在改接线时被丢掉。
        Assert.Equal(1, Count(tray, "NIF_ICON"));
        Assert.True(tray.Contains("_hIcon != IntPtr.Zero ? NIF_ICON : 0u", StringComparison.Ordinal),
            "托盘的 NIF_ICON 不再是\"取到才交\"（取不到时会交一颗空白句柄）");

        foreach (var (path, text) in ReadRepoUnder("src"))
        {
            var code = Code(text);
            Assert.True(!code.Contains("CreateStarIcon", StringComparison.Ordinal),
                $"{path} 里又出现了自画的那颗星——几何只能有一个出处：scripts/make-app-icon.mjs");
            Assert.True(!code.Contains("CreateIconIndirect", StringComparison.Ordinal),
                $"{path} 里又出现了运行时合成图标句柄那条路（同一颗星画两遍）");
        }
    }

    /// <summary>
    /// 安装包与产树脚本读的必须是<b>树里那一份</b>，不是仓库里的第二条路径。
    /// <para><c>SetupIconFile={#SourceDir}\StarMark.ico</c> 让"打包时看到的星"与"装完跑起来的星"是同一个文件；
    /// 指回 <c>assets\app</c> 就变成两处出处（那条路径一搬，向导与运行时各换一颗而谁都不红）。
    /// 产树那一步是这条链上唯一"没人喊"的地方：<c>dotnet publish</c> 天生会漏东西（坑表 #237），
    /// 漏了图标不会崩，只是标题栏回到通用图、托盘连图标都不交，所以脚本必须自己拦下来。</para>
    /// </summary>
    [Fact]
    public void TheInstallerReadsTheCopyFromThePayloadTreeAndTheTreeRefusesToShipWithoutIt()
    {
        // .iss 的注释有两种：段注释 ; 与脚本注释 //；这里只把段注释抹掉（禁项指的是令行里的路径）。
        var directives = string.Join("\n", ReadRepoFile(Iss).Split('\n')
            .Select(l => l.TrimEnd('\r')).Where(l => !l.TrimStart().StartsWith(';')));
        Assert.Equal(1, Count(directives, "SetupIconFile"));
        Assert.True(directives.Contains(@"SetupIconFile={#SourceDir}\StarMark.ico", StringComparison.Ordinal),
            "安装包的图标不再读载荷树那一份（那就会与运行时读的那份分岔）");
        Assert.True(!directives.Contains("assets", StringComparison.Ordinal),
            "安装包的图标指回了仓里第二条路径");

        var publish = PsCode(ReadRepoFile(PublishTree));
        Assert.Equal(1, Count(publish, "StarMark.ico"));
        Assert.Equal(1, Count(publish, "StarMarkTray.ico"));
        var loop = Between(publish, "foreach ($ico in @(", @"Write-Host ""产物树");
        Assert.Contains("Test-Path", loop, StringComparison.Ordinal);
        Assert.Equal(1, Count(loop, "throw"));   // 少了图标必须当场停下，不能"记一条警告继续出货"
    }
}
