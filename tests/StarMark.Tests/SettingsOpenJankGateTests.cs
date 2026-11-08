#nullable enable
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 打开设置页卡顿闸门（片242，扫源码不跑界面）。
/// <para>
/// 现场读数（starmark-20261108.log）：打开设置页后 ~374ms（350ms 去抖到期）发一轮「设置已保存」——
/// 用户一个字没改；同窗 5s 内 backdrop ApplyBackdrop 59 条（≈双份×主窗+12 组件窗），
/// 每条 3 个 DWM 跨进程互操作全在 UI 线程，正是 16:11 那两轮「阻塞 1.4s・CPU 仅 16ms」等待型现场的来源。
/// 这里守四半，缺一算没做：
/// ① 去抖保存链 ⛔ 直调材质刷新（ViewModel.Save 尾部已带同款即时应用，再刷＝一轮双份）；
/// ② 保存即生效那条 ⛭ 被顺手删掉（删的是直调冗余，不是即时应用本身）；
/// ③ 装载抑制窗口到 Loaded 才解除（ctor→Loaded 之间没有一件变化是用户操作）；
/// ④ 语言下拉值没变（zh→zh）只记取证 ⛔ 写盘（打开设置页不再带一发同步原子替换）。
/// </para>
/// </summary>
public sealed class SettingsOpenJankGateTests
{
    private static string PageCode() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.xaml.cs"));
    private static string I18nCode() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/Views/SettingsPage.I18n.cs"));
    private static string VmCode() => SourceGate.Code(SourceGate.ReadRepoFile("src/StarMark.UI/ViewModels/SettingsPageViewModel.cs"));

    /// <summary>去抖 Tick 里只许「保存＋托盘」两件事：材质刷新由 Save 尾部那份负责，这里再直调就是双份。</summary>
    [Fact]
    public void TheDebouncedTickDoesNotDoubleApplyAppearance()
    {
        var tick = SourceGate.MethodBody(PageCode(), "private DispatcherTimer BuildAutoSaveTimer()");
        Assert.DoesNotContain("RefreshAppearance", tick);
        Assert.Contains("SaveCommand.Execute", tick);   // 保存本身仍在（删的只是冗余刷新，不是保存链）
        Assert.Contains("ApplyTraySettings", tick);
    }

    /// <summary>保存即生效那条不许被顺手删掉：它是所有保存出口的材质即时应用（保存→主窗＋组件窗重挂材质）。</summary>
    [Fact]
    public void SaveStillAppliesAppearanceAtItsTail()
    {
        var save = SourceGate.MethodBody(VmCode(), "private void Save()");
        Assert.Contains("RefreshAppearanceAsync", save);
        Assert.Contains("RefreshThemeIcon", save);
    }

    /// <summary>装载抑制到 Page_Loaded 才解除：此前（ctor→Loaded）的一切属性变化都是装载回灌，⛔ 触发去抖保存。</summary>
    [Fact]
    public void LoadSuppressionLastsUntilThePageIsLoaded()
    {
        var page = PageCode();
        var loaded = SourceGate.MethodBody(page, "private void Page_Loaded(object sender, RoutedEventArgs e)");
        Assert.Contains("_suppressSave = false", loaded);
        var loader = SourceGate.MethodBody(page, "private void LoadFromStoreSilently()");
        Assert.DoesNotContain("_suppressSave = false", loader);   // ⛔ 装载中途提前解除
    }

    /// <summary>语言下拉值没变（zh→zh）＝装载回填或重复选择：取证照记，写盘与生效链跳过。</summary>
    [Fact]
    public void UnchangedLanguageChoiceSkipsTheWrite()
    {
        var handler = SourceGate.MethodBody(I18nCode(), "private void LanguageSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("界面语言写入取证", handler);   // 取证日志仍在（P-170 口径：读数照记）
        Assert.Contains("string.Equals(previous, code, StringComparison.Ordinal)", handler);
        Assert.Contains("return", handler);
        // 早退必须挡在写盘前面：先比较再 SetUiLanguageCode，顺序反了这条闸就是空话
        Assert.True(handler.IndexOf("string.Equals(previous, code", System.StringComparison.Ordinal)
                    < handler.IndexOf("store.SetUiLanguageCode(code)", System.StringComparison.Ordinal),
                    "「值没变早退」必须排在写盘之前");
    }
}
