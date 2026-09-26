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
/// 文字标注：就地输入（一条文字框的全部生命周期）。
/// <b>与同目录其余 CaptureOverlayWindow.*.cs 是同一个类</b>（partial，按「一件一个文件」拆开，不是新抽象层）。
/// </summary>
public sealed partial class CaptureOverlayWindow
{
    // ────────── 文字标注：就地输入 ──────────

    /// <summary>
    /// 就地开一个输入框。<b>点一下就该能直接打字</b>（用户的原话是"不需要再次点击文字编辑框内区域才能输入"），
    /// 所以这里管三件事：框摆在哪儿、字色描边、以及<b>键盘焦点真的落进去</b>。
    /// <para><paramref name="editing"/> 非空＝改已经写好的那一条：框里带上原文、光标停在末尾，
    /// 落笔时替换那一条而不是再加一条。位置用它<b>当下</b>的包围盒左上角——那条字可能已经被拖走过或放大过。</para>
    /// </summary>
    private void BeginTextEdit(PixelPoint local, Annotation? editing = null, int? index = null)
    {
        _editingText = true;
        _editingIndex = editing is null ? null : index;
        // 颜色跟着"这一条自己的颜色"，不跟着调色板：用户改旧字时可能已经换成了别的颜色，
        // 输入框显示红的、落笔仍是白的＝"编辑时和编辑完不是一份字"（真机反馈的红白两层之一）。
        _editorColourBgra = editing?.EffectiveColorBgra ?? ColourBgra;
        // "让位"必须长在开框这一步里，不能指望调用方随后重烤：Rebake 的那条排除只在下标已经写进字段之后
        // 才生效，而两条入口（命中测试、拖动没动）原先都只画把手 ⇒ 底下那份旧字一直留在画面里，
        // 与输入框叠成真机反馈了两轮的"红白两层"。
        Rebake();
        _textAnchor = local;
        // 摆到"真画出去那一格"的左上角（Bounds() 是旋转后的外接框，转过 90° 时它会跑到字外面的空处）
        var at = editing is { } mark ? mark.TransformedPoints()[0] : local;
        var (x, y) = LocalToDip(at);
        // 宽度上限跟着屏幕收：写满一行的字被 MaxWidth 截断＝用户看到的成品与框里不一样。
        // 换行只由 Enter 决定（XAML 里 AcceptsReturn），不许自动折行——编辑框折了而 GDI 不折，就是两张图。
        TextEditor.MaxWidth = Math.Max(180, _monitor.Width / _scale - 20);
        // 靠右边/下边点击时把输入框拉回屏内：越界就等于"输入框跑屏外了，打不了字"
        var hostX = Math.Clamp(x, 0, Math.Max(0, _monitor.Width / _scale - 40));
        var hostY = Math.Clamp(y, 0, Math.Max(0, _monitor.Height / _scale - 40));
        TextEditorHost.Margin = new Thickness(hostX, hostY, 0, 0);
        // 旋转过的文字回编辑时输入框跟着转同一个角度（用户口径："旋转文字时文字编辑框也一同旋转"），
        // 轴是字块自己的中心——绕别的点转，框里的字就和烤出去的那份对不上位置。
        if (editing is { } rotated && rotated.Rotation != 0)
        {
            var box = rotated.Bounds();
            var (cx, cy) = LocalToDip(new PixelPoint(box.X + box.Width / 2, box.Y + box.Height / 2));
            TextEditorHost.RenderTransform = new RotateTransform
            {
                Angle = rotated.Rotation,
                CenterX = cx - hostX,
                CenterY = cy - hostY,
            };
        }
        else TextEditorHost.RenderTransform = null;
        TextEditorHost.Visibility = Visibility.Visible;
        // 字号走的是"底图像素 → 屏幕像素 → DIP"两层：只除 `_scale` 的话，放大过的贴图里
        // 输入框中的字会比烤进去的那份小一个倍率（所见非所得），2.5× 上就是小 2.5 倍。
        // 新写一行用"细/中/粗"选出的字号档（批次 PV：文字的粗细档＝字号，输入＝成品，所见即所得）。
        TextEditor.FontSize = (editing?.DrawFontHeight
            ?? Annotation.ThicknessFor(AnnotationTool.Text, _weightIndex)) * _sourceScale / _scale;
        ApplyEditorAccent();
        TextEditor.Text = editing?.Text ?? string.Empty;
        TextEditor.SelectionStart = TextEditor.Text.Length;   // 改字＝光标落在末尾：退格与接着打字都在手边
        TakeEditorFocus();
    }

    /// <summary>
    /// 把键盘焦点真的送进输入框。刚把宿主从 Collapsed 改成 Visible 的<em>同一帧</em>里
    /// <c>Focus()</c> 会当场返回 false（元素还没量过），键于是全落到遮罩那一层——
    /// 用户看到的就是"框出来了，但必须再点一下框里才能打字"。所以：补一次布局再要，
    /// 仍要不到就在接下来几帧里重试；真拿不到才说实话（静默失效是最难查的一类）。
    /// </summary>
    private void TakeEditorFocus(int triesLeft = 3)
    {
        TextEditor.UpdateLayout();
        if (TextEditor.Focus(FocusState.Programmatic)) return;
        if (triesLeft > 1 && Root.DispatcherQueue.TryEnqueue(() => TakeEditorFocus(triesLeft - 1))) return;
        if (!_editingText) return;        // 用户已改去点别处：这时再说"没焦点"是假警报
        ShowError("这一行字还没拿到键盘焦点：点一下那个描边的输入框再打字（Enter 换行，Esc 结束编辑）");
    }

    /// <summary>
    /// 就地输入那一框的字色与描边：<b>只有这一处</b>在说"用哪个颜色"。
    /// 底板是近乎透明的（真机反馈："点击后不应出现黄色矩形，最好是透明但描边的边框"——
    /// 实色黄底会把正要看的画面盖掉），所以边界全靠这条描边认出来，描边跟着当前字色走，
    /// 在深色截图与浅色截图上都看得出来。
    /// </summary>
    private void ApplyEditorAccent()
    {
        // 新写的一行跟着当前调色板走；改旧字时用那条字自己的颜色（换调色板不该改旧字的颜色）
        var brush = new SolidColorBrush(ToColor(_editingIndex is null ? ColourBgra : _editorColourBgra));
        TextEditor.Foreground = brush;
        TextEditorHost.BorderBrush = brush;
    }

    /// <summary>
    /// 输入框里的键：<b>Enter 换行</b>（交给 TextBox 自己插行，不再当"落笔"），Esc 结束编辑并保住已打的字。
    /// <para>Enter 以前是提交，用户想分两行就只能写完一条再点别处开第二条——真机期望是"编辑过程中可以通过
    /// enter 进行文字换行继续编辑"。提交出口现在是：点选区别处、切工具、点动作按钮、或 Esc。</para>
    /// </summary>
    private void TextEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;                 // Esc 在输入框里＝结束这一行字的编辑，不是取消整场截图
        EndTextEditing(commit: true);
    }

    /// <summary>
    /// 结束就地输入。<paramref name="commit"/> 为 false 只用于"换选区"（整屏都要作废的那条路）——
    /// <b>Esc 是落笔不是丢弃</b>：用户要的是"退出文字编辑去选别的工具"，不是"把我打的字变没"。
    /// <para>改旧字那一路只动文本、不动变换（位置/字号/角度都留着），并且删空了就是删掉那条——
    /// 留一条"没有字"的文字标注既画不出东西又占着选中位，只会让人以为程序卡了一条。</para>
    /// </summary>
    private void EndTextEditing(bool commit)
    {
        if (!_editingText) return;
        _editingText = false;
        TextEditorHost.Visibility = Visibility.Collapsed;
        var index = _editingIndex;
        _editingIndex = null;
        // 开框时被从画面里藏掉的那一条（见 Rebake）要在这一刻回来，否则它就永久隐身：一条画不出又点不着的字。
        // 只在下标非空时才需要重烤——新写一行的那一路什么都没藏，白烤一张整幅选区不值。
        if (!commit)
        {
            if (index is not null) Rebake();
            return;
        }
        var text = TextEditor.Text.TrimEnd();

        if (index is { } existing)
        {
            if (existing >= _history.Count) return;       // 编辑期间历史被动过（撤销/删除）：不猜下标，宁可不改
            if (text.Length == 0) { _selected = existing; DeleteSelected(); return; }
            _history.ReplaceAt(existing, _history.Marks[existing] with { Text = text });
            Rebake();
            DrawSelectionHandles();
            return;
        }
        if (text.Length == 0) return;
        _history.Add(new Annotation(AnnotationTool.Text, new[] { _textAnchor }, ColourBgra, ThicknessForTool)
        {
            Text = text,
            // 字号＝用户在浮层里选的那一档（批次 PV：文字的细/中/粗＝字号 16/22/32），
            // 与输入框开框时用的同一个数——输入时的文字大小就是编辑后的文字大小。
            FontHeight = Annotation.ThicknessFor(AnnotationTool.Text, _weightIndex),
        });   // 轴由模型按字块中心现算（Annotation.Origin），界面不自己钉变换轴
        _selected = _history.Count - 1;   // 打完字紧接着就是"挪个位置/改个字号"：那一条直接在手边
        Rebake();
        DrawSelectionHandles();
    }

}
