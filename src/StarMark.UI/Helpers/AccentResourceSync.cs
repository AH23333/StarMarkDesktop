#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StarMark.Core.Appearance;
using Windows.UI;

namespace StarMark.UI.Helpers;

/// <summary>
/// 把「主题色＝自定义」那颗染料<b>交给框架自己那一族资源</b>（批次 WI-82，把 P-213 剩下的那一半收掉）。
/// <para>
/// <b>为什么 WI-81 那一层不够</b>：<see cref="ThemeBrush"/> 管的是<b>代码按键取画笔</b>的那几处；
/// 而 XAML 里按 <c>{ThemeResource AccentFillColorDefaultBrush}</c> 直接取的那三处，与控件<b>内部模板</b>
/// 自己问的那一族（开关的轨道、滑杆的拇指与已填段、进度条、勾选与单选、焦点框、页签选中那一横）
/// 走的是 <c>SystemAccentColor*</c> 这套<b>颜色</b>资源——不覆盖它，表现就是"改了颜色只有一半跟着变"。
/// </para>
/// <para>
/// <b>写法</b>：把派生色写进 <c>Application.Current.Resources</c> 的<b>主题字典</b>（Light / Default 两桶），
/// 这是 ThemeResource 查找里能压过框架内置字典的最高一层。⛔ 写进 <c>MergedDictionaries</c> 里那份
/// <c>XamlControlsResources</c>（那是框架的档，运行期改它＝动别人的资源表）。
/// </para>
/// <para>
/// <b>改回「随系统」时把这几颗整颗摘掉</b>（⛔ 留一颗"看起来一样"的值）——留着就把系统强调色永久钉死在
/// 摘除那一刻的读数上，而他换 Windows 强调色时那一半再也不会跟（与 ✅WI-81 那颗缓存同一口径：读者与档同源）。
/// </para>
/// </summary>
internal static class AccentResourceSync
{
    /// <summary>框架主题字典的两个桶名（内置字典只有 Light / Default，⛔ 写 "Dark" 那颗没人读）。</summary>
    private static readonly string[] Buckets = { "Light", "Default" };

    /// <summary>这一族颜色资源的完整名单：摘的时候要一颗不剩，写的时候一颗不多。</summary>
    private static readonly string[] Keys =
    {
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
        "SystemAccentColorPrimary", "SystemAccentColorSecondary", "SystemAccentColorTertiary",
    };

    /// <summary>这颗染料此刻是否已经压住了框架那族（保存时据此决定要不要惊动各窗重解一遍）。</summary>
    public static bool Active { get; private set; }

    /// <summary>
    /// 交上去（<paramref name="custom"/> 为 null＝摘干净，一切回框架默认）。
    /// <para>⛔ 在这里读设置档：调用点只有两颗——启动（首颗笔光之前）与保存（落盘那一刻），
    /// 与 <see cref="ThemeAccent.Apply"/> 同拍，读者与设置档永远同源。</para>
    /// </summary>
    public static void Apply(Color? custom)
    {
        var app = Microsoft.UI.Xaml.Application.Current?.Resources;
        if (app is null) return;

        foreach (var bucket in Buckets)
        {
            var dict = Bucket(app, bucket);
            if (dict is null) continue;

            if (custom is null)
            {
                foreach (var key in Keys) dict.Remove(key);
                continue;
            }

            var (light1, light2, dark1, dark2, dark3) = AppPalette.AccentRamp(custom.Value);
            var isLight = bucket == "Light";
            dict["SystemAccentColor"] = custom.Value;
            dict["SystemAccentColorLight1"] = light1;
            dict["SystemAccentColorLight2"] = light2;
            dict["SystemAccentColorDark1"] = dark1;
            dict["SystemAccentColorDark2"] = dark2;
            dict["SystemAccentColorDark3"] = dark3;
            // Primary/Secondary/Tertiary 是"这一桶里模板真正取的那三颗"：浅色桶取暗档、深色桶取亮档，
            // 与框架自己的取向一致（⛔ 两桶给同一颗——那样浅色界面会拿到一颗为深色调的色）。
            dict["SystemAccentColorPrimary"] = isLight ? dark1 : light1;
            dict["SystemAccentColorSecondary"] = isLight ? dark2 : light2;
            dict["SystemAccentColorTertiary"] = isLight ? light1 : dark1;
        }

        Active = custom is not null;
    }

    private static ResourceDictionary? Bucket(ResourceDictionary app, string bucket)
    {
        if (app.ThemeDictionaries.TryGetValue(bucket, out var existing)
            && existing is ResourceDictionary found) return found;
        var created = new ResourceDictionary();
        app.ThemeDictionaries[bucket] = created;
        return created;
    }

    /// <summary>
    /// 让某一扇窗把 <c>{ThemeResource}</c> 重解一遍：ThemeResource 只在<b>主题真的换了</b>那一刻重算，
    /// 而"只换染料、不换深浅"不会产生那次通知 ⇒ 翻一个来回再翻回来（同 ✅WI-1 那颗"换语言即重贴"的节拍）。
    /// <para>⛔ 常驻调用：只有染料<b>变了</b>那一拍才惊动（拖别的滑杆时翻主题＝肉眼可见的闪）。</para>
    /// </summary>
    public static void Retheme(Window window)
    {
        if (window.Content is not FrameworkElement root) return;
        var want = root.RequestedTheme;
        if (want == ElementTheme.Default) want = ThemeManager.ResolveEffectiveTheme(ThemePreference.Default);
        root.RequestedTheme = want == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        root.RequestedTheme = want;
    }
}
