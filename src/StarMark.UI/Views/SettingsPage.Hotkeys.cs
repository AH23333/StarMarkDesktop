#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using StarMark.Abstractions;
using StarMark.Abstractions.Backup;
using StarMark.Core.Backup;
using StarMark.Core.Hotkeys;
using StarMark.Core.Widgets;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using StarMark.UI.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace StarMark.UI.Views;

/// <summary>
/// SettingsPage 的这一段——全局快捷键这一头：行与分组怎么长出来、录制那把钩子、保存/放弃/恢复默认与注册失败的回执。
/// <para>按访问面拆出来的 partial：<b>不持有任何状态</b>——字段与构造都住在主文件里，这里只放"同一件事的那几条出口"，好让主文件回到能一眼看完的尺寸。</para>
/// </summary>
public sealed partial class SettingsPage
{

    private HotkeyService? HotkeySvc()
        => App.Services.GetRequiredService<HotkeyService>();

    /// <summary>
    /// 页面活着的时候盯住"占用中"清单：注册是自动重试的，占用者一退出提示就该消失。
    /// 不刷新等于让一句已经过期的"被占用"留在界面上——那从"看得见失败"变成"撒谎的旧提示"。
    /// </summary>
    private void OnRegistrationStateFlushed(System.Collections.Generic.IReadOnlyList<HotkeyRegistrationFailure> _)
        => MarkRegistrationFailures();

    private void BuildHotkeyRows()
    {
        // 有未保存的录制结果时保留内存副本，避免布局增删等重建把用户刚录的键冲掉
        if (!_hotkeysDirty)
        {
            _hotkeyBindings.Clear();
            foreach (var kv in new SettingsStore().GetHotkeyBindings())
                _hotkeyBindings[kv.Key] = kv.Value;
        }

        _layouts = WidgetManager()?.GetLayouts() ?? new List<WidgetLayout>();
        HotkeyRowsItems.Clear();
        if (HotkeyGroups is not null) HotkeyGroups.Children.Clear();

        // 先按动作建行（同时进扁平列表供冲突标注），再按分类分组
        var byCat = new Dictionary<string, List<HotkeyRow>>();
        foreach (var action in HotkeyActions.All(_layouts))
        {
            var bound = _hotkeyBindings.TryGetValue(action, out var g) && !g.IsEmpty;
            var row = new HotkeyRow
            {
                Action = action,
                ActionName = HotkeyActions.DisplayName(action, _layouts),
                BindingText = bound ? HotkeyDisplay.Display(g!) : UnsetText,
            };
            HotkeyRowsItems.Add(row);
            var cat = HotkeyActions.CategoryOf(action, _layouts);
            if (!byCat.TryGetValue(cat, out var list)) { list = new List<HotkeyRow>(); byCat[cat] = list; }
            list.Add(row);
        }

        // 按固定顺序把每个分类渲染为一个可折叠的 Expander（多级菜单），消除扁平长列表的重复感
        if (HotkeyGroups is not null)
        {
            foreach (var cat in HotkeyActions.CategoryOrder)
                if (byCat.TryGetValue(cat, out var rows))
                    HotkeyGroups.Children.Add(BuildCategoryExpander(cat, rows));
        }
        RefreshConflictMarks();
        MarkRegistrationFailures();   // 行是新建的，注册失败要在建行时就标出来（否则开页看不到原因）
    }

    /// <summary>把一个分类的动作行装进一个可折叠 <see cref="Expander"/>（标题=分类名，内容=该类的快捷键行）。</summary>
    private Expander BuildCategoryExpander(string category, IList<HotkeyRow> rows)
    {
        var repeater = new ItemsRepeater
        {
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)Resources["HotkeyRowTemplate"],
        };
        repeater.Layout = new StackLayout { Spacing = 4 };

        var count = rows.Count(r => !string.Equals(r.BindingText, UnsetText, StringComparison.Ordinal));
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = category,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black),
        });
        header.Children.Add(new TextBlock
        {
            Text = $"（{count} 项已绑定）",
            FontSize = 11,
            Opacity = 0.7,
            Foreground = Brush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Expander
        {
            Header = header,
            Content = repeater,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4),
            // 一律折叠：进这一页不等于"要看这两类"。原先每次点「快捷键」都把「主界面」「组件总控」摊开，
            // 用户要看的在底下、却先被两屏不相关的行顶下去（展开与否是用户的事）。
            IsExpanded = false,
        };
    }

    /// <summary>把「同一组合绑定了多个动作」就地标注到每一行（不弹窗，用户可直接忽略）。</summary>
    private void RefreshConflictMarks()
    {
        var byGesture = HotkeyBindings.GetConflicts(_hotkeyBindings)
            .ToDictionary(c => c.Key, c => c.Actions);

        foreach (var row in HotkeyRowsItems)
        {
            if (!_hotkeyBindings.TryGetValue(row.Action, out var g) || g.IsEmpty)
            {
                row.ConflictText = string.Empty;
                continue;
            }
            if (!byGesture.TryGetValue(HotkeyGesture.GestureKey(g), out var actions) || actions.Count <= 1)
            {
                row.ConflictText = string.Empty;
                continue;
            }
            var others = actions.Where(a => a != row.Action)
                                .Select(a => HotkeyActions.DisplayName(a, _layouts))
                                .ToList();
            row.ConflictText = others.Count == 0
                ? string.Empty
                : "⚠ 与其它动作共用该组合：" + string.Join("、", others);
        }
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string action) return;

        // 录制按钮保留焦点，若用户录的正是 Space / Enter，按键会顺带再次触发按钮的 Click。
        // 这里在录制结束后的短时间内忽略重复点击，避免「录完立刻又进入录制」。
        if (DateTimeOffset.UtcNow < _ignoreClickUntil) return;

        var row = HotkeyRowsItems.FirstOrDefault(r => r.Action == action);
        if (row is null) return;

        // 再次点击同一项：已录了键 → 当作「确认结束」；一个键都没录 → 取消录制
        if (_recordingAction == action)
        {
            if (RecordedKeyCount > 0) FinishRecording();
            else CancelRecording();
            return;
        }

        StopRecording(resetText: true);      // 先把上一个录制还原
        BeginRecording(action, btn, row);
    }

    /// <summary>进入录制态：先装底层钩子（装不上就说明原因、保持原显示），再清空按钮文字（不高亮）并开始回显按键。</summary>
    private void BeginRecording(string action, Button btn, HotkeyRow row)
    {
        // 先装钩子、再进录制态：钩子装不上就一个按键都收不到，而原先的顺序会先把按钮文字清空，
        // 用户看到的是"按了没反应、原来的快捷键也不显示了"，而不是一句"录制现在不可用"。
        if (!_hkHook.TryStart(out var hookError))
        {
            var why = HotkeyErrorText.HookInstallFailure(hookError);
            StarMark.Abstractions.StarLog.Warn($"无法安装键盘钩子：{why} ⇒ 快捷键录制不可用");
            RecordingAlert = why + "——因此收不到你按下的键。再点一次这个按钮即可重试。";
            return;
        }
        RecordingAlert = string.Empty;

        _recordingAction = action;
        _recordingButton = btn;
        _recordingRow = row;
        _recordingPrevText = row.BindingText;
        _recordingWasSet = _hotkeyBindings.TryGetValue(action, out var g) && !g.IsEmpty;
        _recordedModifiers = HotkeyModifiers.None;
        _recordedMainKey = 0;

        row.IsRecording = true;
        row.BindingText = string.Empty;                 // 清空文字，保留按钮样式——用户可实时看到按下的键
        row.ConflictText = string.Empty;
        // 注意：不设置 btn.Background，保持默认按钮样式（按用户要求不高亮）

        // 录制期间挂起全局热键，避免按下的键命中已生效的热键（如正在重录「隐藏主界面」
        // 却按下了它的旧键）导致当场触发动作、打断录制。
        HotkeySvc()?.Suspend();
        btn.Focus(FocusState.Programmatic);
    }

    private void OnHookKeyDown(uint vk)
    {
        if (_recordingAction is null || _recordingRow is null) return;
        var row = _recordingRow;

        // Esc：
        // · 已录入若干键 → 确认结束（定稿）；
        // · 一个键都没录、且该动作原本就有绑定 → 视为「清除该快捷键」（按用户要求）；
        // · 一个键都没录、且原本未设置 → 取消录制（无改动）。
        if (vk == (uint)VirtualKey.Escape)
        {
            if (RecordedKeyCount > 0) FinishRecording();
            else if (_recordingWasSet) ClearBinding();
            else CancelRecording();
            return;
        }

        // Backspace / Delete：清除该动作的快捷键（同样需保存后生效）
        if (vk == (uint)VirtualKey.Back || vk == (uint)VirtualKey.Delete) { ClearBinding(); return; }

        // 修饰键：累加到已录组合；仅当已有主键且总键数达到上限才自动结束
        // （纯修饰键不能单独构成热键，不结束，避免录成空绑定）
        if (HotkeyKeys.IsModifier(vk))
        {
            var mod = HotkeyKeys.ModifierOf(vk);
            // 走到这里 mod 必非 None（IsModifier 与取位出自同一张表），只需处理"重复按下同一修饰键"
            if (_recordedModifiers.HasFlag(mod)) { UpdateRecordingText(); return; }
            _recordedModifiers |= mod;
            UpdateRecordingText();
            if (_recordedMainKey != 0 && RecordedKeyCount >= MaxHotkeyKeys) FinishRecording();
            return;
        }

        // 主键：最后一个按下的非修饰键即主键
        _recordedModifiers |= _hkHook.CurrentModifiers;   // 钩子漏记的修饰键补上
        _recordedMainKey = vk;
        UpdateRecordingText();
        if (_recordedMainKey != 0 && RecordedKeyCount >= MaxHotkeyKeys) FinishRecording();
    }

    private void OnHookKeyUp(uint vk)
    {
        if (_recordingAction is null || _recordingRow is null) return;
        UpdateRecordingText();
    }

    /// <summary>当前已录入的按键个数（修饰键个数 + 主键）。</summary>
    private int RecordedKeyCount => CountModifiers(_recordedModifiers) + (_recordedMainKey != 0 ? 1 : 0);

    private static int CountModifiers(HotkeyModifiers m) => HotkeyKeys.ModifierBits.Count(bit => m.HasFlag(bit));

    /// <summary>把已录入的键实时显示在按钮上（未录任何键时保持空白）。</summary>
    private void UpdateRecordingText()
    {
        if (_recordingRow is null) return;
        var mods = HotkeyDisplay.ModifiersDisplay(_recordedModifiers);
        var text = _recordedMainKey == 0
            ? mods
            : (mods.Length == 0 ? HotkeyDisplay.KeyName(_recordedMainKey) : $"{mods} + {HotkeyDisplay.KeyName(_recordedMainKey)}");
        _recordingRow.BindingText = text;
    }

    /// <summary>结束录制：只落到内存待保存字典里，**不注册生效**（需点保存）。</summary>
    private void FinishRecording()
    {
        var action = _recordingAction;
        var row = _recordingRow;
        if (action is null || row is null) { CancelRecording(); return; }

        // 定稿常常就由这最后一次按下触发，而 StopRecording 会在同一个钩子回调里把热键 Resume
        // 回注册态 ⇒ 这次按键若继续派发给系统，会当场命中（可能是别的动作已绑的）热键并执行，
        // 表现为"刚设置完就触发一次"。把这一键吞掉，动作只能等用户点「保存快捷键」后由新键触发。
        _hkHook.SwallowCurrentKeyDown();

        _hotkeyBindings[action] = new HotkeyGesture(_recordedModifiers | HotkeyModifiers.NoRepeat, _recordedMainKey);

        StopRecording(resetText: false);
        row.IsRecording = false;
        var g = _hotkeyBindings[action];
        row.BindingText = g.IsEmpty ? UnsetText : HotkeyDisplay.Display(g);
        _ignoreClickUntil = DateTimeOffset.UtcNow.AddMilliseconds(400);  // 吞掉本键附带的那次 Click
        _hotkeysDirty = true;
        RaiseHotkeyStateChanged();
        RefreshConflictMarks();
    }

    /// <summary>停止钩子并复位录制态；resetText 时把该行显示还原到录制前。</summary>
    private void StopRecording(bool resetText)
    {
        _hkHook.Stop();
        // 录制结束（完成 / 取消 / 切换 / 离开页面）即恢复全局热键
        HotkeySvc()?.Resume();
        if (resetText && _recordingRow is not null)
        {
            _recordingRow.IsRecording = false;
            _recordingRow.BindingText = _recordingPrevText ?? UnsetText;
        }
        _recordingAction = null;
        _recordingButton = null;
        _recordingRow = null;
        _recordingPrevText = null;
        _recordingWasSet = false;
        _recordedModifiers = HotkeyModifiers.None;
        _recordedMainKey = 0;
    }

    private void CancelRecording()
    {
        StopRecording(resetText: true);
        RefreshConflictMarks();
    }

    /// <summary>
    /// 清除当前动作的绑定（不立即生效，需保存）。
    /// <para>
    /// 写的是<b>空手势</b>而不是从字典里 Remove：磁盘上必须留下"用户明确清过这一条"的痕迹，
    /// 否则 <c>GetHotkeyBindings</c> 的"默认 + 已存"合并会因为键缺失而把默认手势补回来 ⇒
    /// 表现为「Ctrl+Alt+Space（切换主界面）清不掉」，且界面显示与真实注册状态还会不一致。
    /// </para>
    /// </summary>
    private void ClearBinding()
    {
        if (_recordingAction is null) return;
        var row = _recordingRow;
        // 同 FinishRecording：清除也由一次按下（Esc / Backspace / Delete）触发，而 StopRecording
        // 会在同一回调里恢复热键注册 ⇒ 不吞掉就会让这一键当场命中某个已生效的组合并执行动作。
        _hkHook.SwallowCurrentKeyDown();
        _hotkeyBindings[_recordingAction] = new HotkeyGesture(HotkeyModifiers.NoRepeat, 0);
        StopRecording(resetText: false);
        if (row is not null) row.BindingText = UnsetText;
        _hotkeysDirty = true;
        RaiseHotkeyStateChanged();
        RefreshConflictMarks();
    }

    // ── 保存 / 撤销 / 恢复默认 ──

    /// <summary>把内存里的绑定真正写盘并注册生效（快捷键录制结果必须走这一步）。</summary>
    private void SaveHotkeys_Click(object sender, RoutedEventArgs e)
    {
        StopRecording(resetText: true);
        ApplyHotkeyBindings();
        _hotkeysDirty = false;
        RaiseHotkeyStateChanged();
        BuildHotkeyRows();
    }

    /// <summary>放弃未保存的录制结果，回到磁盘上的绑定。</summary>
    private void DiscardHotkeys_Click(object sender, RoutedEventArgs e)
    {
        StopRecording(resetText: true);
        _hotkeysDirty = false;
        RaiseHotkeyStateChanged();
        BuildHotkeyRows();
    }

    private async void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        // 破坏性操作：先确认（统一走外部居中窗口：按用户主题着色、可拖动、不可重复）
        var confirm = await CenteredDialog.ConfirmAsync(
            "恢复默认快捷键",
            "将清空所有自定义快捷键，只保留默认的「Ctrl + Alt + Space（切换主界面）」。此操作不可撤销。",
            primaryText: "恢复默认", cancelText: "取消",
            owner: App.MainWindow, dedupeKey: "resethotkeys");
        if (!confirm) return;

        StopRecording(resetText: true);
        var defaults = HotkeyBindings.Defaults();
        _hotkeyBindings.Clear();
        foreach (var kv in defaults) _hotkeyBindings[kv.Key] = kv.Value;
        ApplyHotkeyBindings();      // 确认后立即生效（用户显式确认过）
        _hotkeysDirty = false;
        RaiseHotkeyStateChanged();
        BuildHotkeyRows();
    }

    /// <summary>存在尚未保存的快捷键改动（录制完成 / 清除后为 true）。</summary>
    public bool HasUnsavedHotkeys => _hotkeysDirty;

    private void RaiseHotkeyStateChanged() => RaisePropertyChanged(nameof(HasUnsavedHotkeys));

    /// <summary>保存绑定 → 热更新 HotkeyService（冲突信息由行内标记呈现，不弹窗）。</summary>
    private void ApplyHotkeyBindings()
    {
        var settings = new SettingsStore();
        settings.SaveHotkeyBindings(_hotkeyBindings);

        if (App.Services.GetRequiredService<HotkeyService>() is HotkeyService hotkey)
            hotkey.ApplyBindings(ViewModel.EnableGlobalHotKey
                ? settings.GetRegisterableHotkeyBindings()   // 刚写盘的那一份 + 功能总开关的闸门（关掉的那批不占键）
                : new Dictionary<string, HotkeyGesture>());
        MarkRegistrationFailures();   // 保存后立刻把"没注册上"的那几条说清楚
        // 「拓展功能」页那一览跟着重算：改完键却还显示旧键位，比不显示更容易误导人
        ViewModel.RefreshCanvasHotkeySheet();
    }

    private void EnableGlobalHotKey_Toggled(object sender, RoutedEventArgs e)
    {
        // 即时生效：总开关关闭时清空所有热键，开启时应用当前绑定
        var settings = new SettingsStore();
        settings.SaveEnableGlobalHotKey(ViewModel.EnableGlobalHotKey);
        ApplyHotkeyBindings();
    }

    /// <summary>
    /// 把"注册失败（组合键已被其它程序占用）"说出来：逐行标原因 + 一条带「重试注册」的汇总。
    /// <para>
    /// 原先这一失败只写 <c>StarLog.Warn</c>：那一行仍显示用户刚录的组合、「保存快捷键」也算成功，
    /// 用户看到的只是"设了却永远不生效"，且没有任何地方告诉他为什么（P-54：失败要看得见、点得动）。
    /// </para>
    /// </summary>
    private void MarkRegistrationFailures()
    {
        var failed = HotkeySvc()?.RegistrationFailures ?? Array.Empty<HotkeyRegistrationFailure>();
        var reasons = new Dictionary<string, string>();
        foreach (var f in failed) reasons[HotkeyGesture.GestureKey(f.Gesture)] = f.Reason;

        foreach (var row in HotkeyRowsItems)
        {
            // 措辞刻意不是"失败/未注册"：绑定已经存下并在自动重试，这一行只是"此刻还归别人"的事实提示。
            row.RegisterErrorText =
                _hotkeyBindings.TryGetValue(row.Action, out var g) && !g.IsEmpty
                && reasons.TryGetValue(HotkeyGesture.GestureKey(g), out var why)
                    ? $"提示：{why}（设置已保存，占用者退出后自动生效）" : string.Empty;
        }

        RegisterErrorSummary = failed.Count == 0
            ? string.Empty
            // 这里不写"被其它程序占用"：Win32 没回答归属，而实际占用方常是本程序自己的另一个实例。
            // 具体是谁由每一条的 Reason 说（那边有 pid 就报 pid），汇总句只说"此刻没归这个实例"这个事实。
            : $"提示：{failed.Count} 个组合键此刻没归这个实例（{string.Join("、", failed.Select(f => $"{HotkeyDisplay.Display(f.Gesture)}：{f.Reason}"))}）。"
              + "这些快捷键已经保存，程序会每 " + HotkeyService.OccupancyRetrySeconds + " 秒自己再注册一次，对方一退出就生效——不需要你做任何事；"
              + "急着现在就要生效可以点「重试注册」，或给这些动作换一个组合再点「保存快捷键」。";
    }

    /// <summary>重试注册：不改绑定内容，只再向系统注册一次（用户可能刚关掉占用该组合键的程序）。</summary>
    private void RetryHotkeyRegister_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeySvc() is { } hotkey && ViewModel.EnableGlobalHotKey)
        {
            var store = new SettingsStore();
            hotkey.ApplyBindings(store.GetRegisterableHotkeyBindings());  // 磁盘上那份，不含未保存的录制；关掉的那批不占键
            ViewModel.RefreshCanvasHotkeySheet();
        }
        MarkRegistrationFailures();
    }

    private string _registerErrorSummary = string.Empty;

    /// <summary>未能注册的组合键汇总（空＝全部注册成功，提示与按钮随之隐藏）。</summary>
    public string RegisterErrorSummary
    {
        get => _registerErrorSummary;
        set
        {
            if (_registerErrorSummary == value) return;
            _registerErrorSummary = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasRegisterErrors));
        }
    }

    public bool HasRegisterErrors => !string.IsNullOrEmpty(_registerErrorSummary);

    private string _recordingAlert = string.Empty;

    /// <summary>键盘钩子装不上时的一次性说明（成功后自动清空）。装不上＝一个按键都收不到。</summary>
    public string RecordingAlert
    {
        get => _recordingAlert;
        set
        {
            if (_recordingAlert == value) return;
            _recordingAlert = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(HasRecordingAlert));
        }
    }

    public bool HasRecordingAlert => !string.IsNullOrEmpty(_recordingAlert);
}
