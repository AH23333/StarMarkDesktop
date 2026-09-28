#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using StarMark.Abstractions;
using StarMark.Abstractions.Capture;
using StarMark.Core.Capture;
using StarMark.Integrations.Capture;
using StarMark.UI.Helpers;
using StarMark.UI.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
// "按下这一想改什么"的枚举归模型（Core.Capture）所有：判定与取值同源，界面不再自己列一份
// （原来那份私有 enum 就是让"拖动一行字变成放大字号"测不到的一半原因——政策在界面，测试引不到）。
using Grab = StarMark.Core.Capture.AnnotationGrab;

namespace StarMark.UI.Views;

/// <summary>
/// 编辑历史：撤销 / 重做 / 清空。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 编辑历史：撤销 / 重做 / 清空 ──────────
    // 三个动作都只是移动历史指针（状态快照在 InkDoc 里），
    // 所以"清空了又撤销回来"和"撤销两步再重做"不需要任何额外代码，也不会残留半条。

    /// <summary>选区阶段的方向键：plain＝平移 1px，Shift＝缩放对应边 1px（物理像素）。</summary>
    private void NudgeRegion(VirtualKey key, bool resize)
    {
        if (_selection is not { } sel) return;
        IntRect next;
        if (resize)
        {
            next = key switch
            {
                VirtualKey.Left => sel with { Width = Math.Max(1, sel.Width - 1) },
                VirtualKey.Right => sel with { Width = sel.Width + 1 },
                VirtualKey.Up => sel with { Height = Math.Max(1, sel.Height - 1) },
                VirtualKey.Down => sel with { Height = sel.Height + 1 },
                _ => sel,
            };
        }
        else
        {
            next = key switch
            {
                VirtualKey.Left => sel with { X = sel.X - 1 },
                VirtualKey.Right => sel with { X = sel.X + 1 },
                VirtualKey.Up => sel with { Y = sel.Y - 1 },
                VirtualKey.Down => sel with { Y = sel.Y + 1 },
                _ => sel,
            };
            // 夹回本屏：方向键把选区推到屏外再按 Enter，裁剪护栏会拿一句错误把人挡住
            next = CaptureGeometry.Intersect(next, _monitor) ?? next;
        }
        _selection = next;
        DrawSelection(next);
        if (_annotating) Rebake();      // 压暗烤在合成图里：方向键挪了框就要跟着重烤（离散按键，不必节流）
    }

    /// <summary>标注阶段的方向键：移动选中的那条标注（plain 1px，Shift 10px）。</summary>
    private void NudgeSelectedMark(VirtualKey key, bool big)
    {
        if (_selected is not int idx || idx < 0 || idx >= _history.Count) return;
        var mark = _history.Marks[idx];
        // 步长按<b>屏幕</b>像素定再换算进底图：贴图缩到 0.2× 时 1 个底图像素只有 0.2 个屏幕像素，
        // 按一下方向键几乎看不见动（Snipaste 的手感是"按一下动一格"）；放大 5× 时反过来会窜格。
        var step = SlopInSource(big ? 10 : 1);
        var dx = key switch
        {
            VirtualKey.Left => -step,
            VirtualKey.Right => step,
            _ => 0,
        };
        var dy = key switch
        {
            VirtualKey.Up => -step,
            VirtualKey.Down => step,
            _ => 0,
        };
        if (dx == 0 && dy == 0) return;
        _history.ReplaceAt(idx, mark.MovedBy(dx, dy));
        Rebake();
    }

    private void Undo()
    {
        if (!_history.Undo()) return;
        DropSelection();                      // 下标随历史移动：旧框指着的是完全另一条标注
        Rebake();
    }

    private void Redo()
    {
        if (!_history.Redo()) return;
        DropSelection();
        Rebake();
    }

    /// <summary>
    /// 全局热键把「撤销／重做」送进<b>这一张贴图</b>（批次 S2-c3 的跨面路由：前台窗是贴图时这一键归它）。
    /// <para><b>正在输入文字时不碰笔迹历史</b>：键位本身不撞车（全局那条是 Ctrl+Alt+Z，输入框撤字是 Ctrl+Z），
    /// 撞的是意图——那一行字还没落定，这时撤销撤到的是<b>它自己</b>（半成型的文字标注整条消失），
    /// 而用户以为自己撤的是刚才那一笔。</para>
    /// </summary>
    internal void HotkeyUndoRedo(bool redo)
    {
        if (_editingText) return;
        if (redo) Redo(); else Undo();
    }

    /// <summary>
    /// 输入框<b>不</b>因失去焦点而结束——这一条就是真机反馈"必须按住鼠标才在输入、松手就算编辑完"的成因：
    /// 按下那一下把焦点给了输入框，松开时焦点回到遮罩那一层，原先挂在 LostFocus 上的落笔于是把这一行当场结掉。
    /// 落笔的时机改由用户看得见的那几个动作明确决定：Enter、点画布别处、切工具、点动作按钮（各自都会调
    /// <see cref="EndTextEditing"/>），Esc 只丢掉这一行。
    /// </summary>
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        Redo();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        EndTextEditing(commit: true);
        _history.Clear();
        DropSelection();
        Rebake();
    }

}
