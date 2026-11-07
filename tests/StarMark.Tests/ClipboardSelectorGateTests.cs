#nullable enable
using System;
using System.Linq;
using StarMark.Abstractions.I18n;
using StarMark.Core.Hotkeys;
using Xunit;

namespace StarMark.Tests;

/// <summary>
/// 剪贴板历史选择器（P-63）的守门：全局热键唤起的键呼浮层，对标 Win+V。
/// <para>
/// 被测的窗口在 <c>StarMark.UI</c>，测试工程刻意不引用它（分层红线），所以浮层行为读源文件比对结构；
/// 键位、目录登记与词条这些纯数据则直接测 Core／Abstractions。
/// </para>
/// <para>钉的形状来自三条真机会坏的路：<b>键位没进默认表</b>（按了没反应）、<b>复制语义被抄了第二份</b>
/// （文字行改了图片行不知道）、<b>浮层收不起来或叠出第二扇</b>（topmost 窗挡住用户刚切换到的程序）。</para>
/// </summary>
public sealed class ClipboardSelectorGateTests
{
    private const string WindowCode = "src/StarMark.UI/Views/ClipboardSelectorWindow.xaml.cs";
    private const string WindowXaml = "src/StarMark.UI/Views/ClipboardSelectorWindow.xaml";
    private const string App = "src/StarMark.UI/App.xaml.cs";

    // ────────── 纯数据：目录登记与用户裁决的默认键 ──────────

    /// <summary>
    /// 用户裁决（P-63）：默认 Ctrl+Alt+V。键位、目录、分类、名字四件必须同时登记——
    /// 漏登记任何一件，症状是"设置页里看得见、按了没反应"或"一行裸 id 的条目"。
    /// </summary>
    [Fact]
    public void ClipboardSelectorIsFullyRegisteredWithTheRuledDefaultCtrlAltV()
    {
        Assert.Contains(HotkeyActions.ClipboardSelector, HotkeyActions.All());
        Assert.Equal(HotkeyActions.Category.Main, HotkeyActions.CategoryOf(HotkeyActions.ClipboardSelector));
        Assert.Equal("hotkey.act.clipboard.selector", LocAssert.KeyOf(HotkeyActions.DisplayMessage(HotkeyActions.ClipboardSelector)));

        var gesture = Assert.Contains(HotkeyActions.ClipboardSelector, HotkeyBindings.Defaults());
        Assert.Equal(0x56u, gesture.VirtualKey);                                   // VK_V
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
            gesture.Modifiers);
    }

    /// <summary>浮层与热键名那三颗词条三语齐备：少一列＝换语言后那一句永远说旧语言。</summary>
    [Fact]
    public void TheSelectorKeysExistInAllThreeLanguages()
    {
        foreach (var key in new[] { "hotkey.act.clipboard.selector", "clip.selector.search", "clip.selector.noMatch" })
        {
            Assert.True(UiStrings.TryGet(key, out var entry), $"{key} 不在词条表里");
            Assert.False(string.IsNullOrWhiteSpace(entry.Zh), $"{key} 缺中文列");
            Assert.False(string.IsNullOrWhiteSpace(entry.En), $"{key} 缺英文列");
            Assert.False(string.IsNullOrWhiteSpace(entry.Ja), $"{key} 缺日文列");
        }
    }

    // ────────── 接线：App 注册 + 反射闸门的兜底 ──────────

    /// <summary>热键注册 handler：漏了这一行＝注册成功、按下去什么也没有（这条链上最难自查的一处）。</summary>
    [Fact]
    public void TheSelectorHotkeyIsWiredInApp()
    {
        Assert.Contains("RegisterHandler(HotkeyActions.ClipboardSelector", SourceGate.ReadRepoFile(App));
    }

    // ────────── 浮层行为（读源码比对结构）──────────

    /// <summary>
    /// 单例＋隐藏不关闭：热键每按一次只可能有一扇浮层在屏幕上。多一扇＝两扇 topmost 互相叠着，
    /// 且旧那扇的筛选词还留着——用户看到的是"上次搜过的那几个"，不是此刻的历史。
    /// </summary>
    [Fact]
    public void TheSelectorWindowIsASingleInstanceHiddenNotClosed()
    {
        var code = SourceGate.ReadRepoFile(WindowCode);
        Assert.Contains("_instance ??= new ClipboardSelectorWindow();", code);
        Assert.Contains("Closed += (_, _) => _instance = null;", code);
        // 隐藏走 WidgetWindow.HideTemporary 同款双保险：AppWindow.Hide() 对某些 owner 不可靠
        Assert.Contains("WindowInterop.ShowWindow(WindowInterop.GetHwnd(this), WindowInterop.SW_HIDE);", code);
        Assert.Contains("AppWindow.Hide();", code);
    }

    /// <summary>
    /// 键盘流转的三条出口都得是真的：<b>Esc 收起</b>（键盘用户唯一的退出）、<b>回车复制</b>（这条链的终点）、
    /// <b>失焦收起</b>（键呼浮层的收起语义——留着不关，下一次唤起它就挡在用户刚切换到的那个程序上面）。
    /// 筛选框自动聚焦也要钉：要再点一下才能搜的键呼浮层等于少了一只手。
    /// </summary>
    [Fact]
    public void TheSelectorHasEscEnterAndLosingFocusExits_AndTheSearchBoxTakesFocusOnShow()
    {
        var code = SourceGate.ReadRepoFile(WindowCode);
        Assert.Contains("case VirtualKey.Escape:", code);
        Assert.Contains("case VirtualKey.Enter:", code);
        // WinUI 3 的 Window 没有 Deactivated 事件：失焦那一下从 Activated 的状态位读（Deactivated＝失焦）
        Assert.Contains("e.WindowActivationState == WindowActivationState.Deactivated", code);
        Assert.Contains("SearchBox.Focus(FocusState.Programmatic);", code);
        // ↑↓ 动的是列表的选中位（焦点留在筛选框继续输入），且回车复制的是"看得见亮着的那条"
        Assert.Contains("case VirtualKey.Up:", code);
        Assert.Contains("case VirtualKey.Down:", code);
        Assert.Contains("List.SelectedIndex = _view.Count > 0 ? 0 : -1;", code);
    }

    /// <summary>
    /// <b>复制语义零重写</b>：浮层只许委托 <c>ClipboardPageViewModel.ReuseAsync</c>——
    /// 在这里再抄一份复制（SetContent／SetBitmap／回声登记）的后果是两条出口各修各的：
    /// 文字行改了图片行不知道，回声漏登记则每复制一次历史就多一条自回声。
    /// </summary>
    [Fact]
    public void TheSelectorDelegatesCopySemantics_NeverReimplementsThem()
    {
        var code = SourceGate.Code(SourceGate.ReadRepoFile(WindowCode));
        Assert.Contains("_vm.ReuseAsync(vm)", code);
        Assert.DoesNotContain("Clipboard.SetContent", code);
        Assert.DoesNotContain("SetBitmap", code);
        Assert.DoesNotContain("NoteClipboardOwnWrite", code);
        // 列表也来自同一只 VM：历史页与浮层是同一份数据的两种摆法，不是两套查询
        // （[RelayCommand] 生成的 LoadCommand——与历史页代码后置同一只命令）
        Assert.Contains("await _vm.LoadCommand.ExecuteAsync(null);", code);
        Assert.Contains("foreach (var entry in _vm.Entries) _all.Add(entry);", code);
    }

    /// <summary>浮层窗上的字全部交键（两句常驻 + 一句失败回执），XAML 里⛔ 出现裸句。</summary>
    [Fact]
    public void TheSelectorXamlWiresItsTextThroughKeys()
    {
        var xaml = SourceGate.ReadRepoFile(WindowXaml);
        Assert.Contains("LocText.Key=\"clip.selector.search\"", xaml);
        Assert.Contains("LocText.Key=\"clip.selector.noMatch\"", xaml);
        Assert.DoesNotContain("PlaceholderText=\"", xaml);   // 占位句要走键，不在 XAML 里现写
    }
}
