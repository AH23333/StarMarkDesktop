#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StarMark.Abstractions;
using StarMark.Abstractions.Ai;
using StarMark.Integrations.Ai;
using StarMark.UI.Helpers;

namespace StarMark.UI.Views;

/// <summary>
/// 设置页的「AI 助手」这一栏（批次 A）。<b>这一批只做到"通道能不能用"</b>：
/// 批量整理标签的界面与编排在批次 B，那里会从这里读同一份 <see cref="AiSettings"/> 与同一个
/// <see cref="AiGateway"/>，而不是再建一套配置。
/// <para>
/// 默认关闭且不发任何请求（§15.5）：这一栏在用户亲手打开开关之前，程序不会把库里的标题摘要发给任何进程。
/// </para>
/// </summary>
public sealed partial class SettingsPage
{
    private bool _aiLoading;
    private bool _aiBusy;
    private CancellationTokenSource? _aiProbeCts;
    private IReadOnlyList<string> _aiModels = Array.Empty<string>();

    private void InitAiSection()
    {
        // 通道下拉由枚举生成：加了新通道就自动多一项，不会出现"枚举里有、界面没有"
        _aiLoading = true;
        try
        {
            AiProviderBox.Items.Clear();
            foreach (var kind in Enum.GetValues<AiProviderKind>())
                AiProviderBox.Items.Add(new ComboBoxItem { Content = AiSettings.NameOf(kind), Tag = kind });

            var stored = App.Services.GetRequiredService<SettingsStore>().LoadAiSettings();
            AiEnabledToggle.IsOn = stored.Enabled;
            AiProviderBox.SelectedIndex = Array.IndexOf(
                Enum.GetValues<AiProviderKind>(), stored.Provider);
            AiModelBox.Text = stored.Model ?? string.Empty;
            AiOllamaUrlBox.Text = stored.OllamaBaseUrl ?? string.Empty;
            AiBaseUrlBox.Text = stored.BaseUrl ?? string.Empty;
            AiKeyBox.Password = stored.ApiKey ?? string.Empty;
        }
        finally
        {
            _aiLoading = false;
        }
        ShowAiProblem();
    }

    /// <summary>把界面上这一组控件读成一份配置。<b>只有这一个地方读控件</b>：
    /// 分散着读就会出现"某一格改了但没进保存"的漏。</summary>
    private AiSettings ReadAiSettings() => new(
        Enabled: AiEnabledToggle.IsOn,
        Provider: AiProviderBox.SelectedItem is ComboBoxItem { Tag: AiProviderKind kind }
            ? kind : AiProviderKind.Ollama,
        Model: AiModelBox.Text,
        ApiKey: AiKeyBox.Password,
        OllamaBaseUrl: AiOllamaUrlBox.Text,
        BaseUrl: AiBaseUrlBox.Text);

    private void AiEnabled_Toggled(object sender, RoutedEventArgs e) => PersistAiAndShow();

    private void AiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => PersistAiAndShow();

    private void AiField_TextChanged(object sender, RoutedEventArgs e) => PersistAiAndShow();

    private void PersistAiAndShow()
    {
        if (_aiLoading) return;           // 装载时控件逐个置位，不能每一步都写盘一次
        var settings = ReadAiSettings();
        App.Services.GetRequiredService<SettingsStore>().SaveAiSettings(settings);
        ShowAiProblem(settings);
    }

    /// <summary>未测试过就把"能不能用"的判据显示出来：<b>配置不成立时不该让用户去点「测试连接」才发现</b>。</summary>
    private void ShowAiProblem(AiSettings? settings = null)
    {
        settings ??= ReadAiSettings();
        AiStatusText.Text = settings.Problem() is { } bad
            ? "配置还不能用：" + bad
            : settings.DescribeWhere() + " —— 可以点「测试连接」确认服务真的答话。";
    }

    /// <summary>
    /// 「测试连接」：问服务有哪些模型，并核对模型名在不在。
    /// <para>抓取期间同一个按钮变成「取消」：本机模型冷启动时这一下可能要十几秒，
    /// 只把按钮置灰让人干等，等于把唯一入口变成一个不能中断的黑盒。</para>
    /// </summary>
    private async void AiProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy)
        {
            _aiProbeCts?.Cancel();
            AiStatusText.Text = "正在取消…";
            return;
        }

        var settings = ReadAiSettings();
        _aiBusy = true;
        AiProbeButton.Content = "取消";
        AiFillModelButton.Visibility = Visibility.Collapsed;
        AiStatusText.Text = "正在问服务有哪些模型…";
        var cts = new CancellationTokenSource();
        _aiProbeCts = cts;
        try
        {
            var probe = await App.Services.GetRequiredService<AiGateway>().ProbeAsync(settings, cts.Token);
            AiStatusText.Text = probe.Report;

            // "通了但模型名不对"要能一步修好：把服务侧的名字直接递过去，而不是让用户自己抄一遍
            _aiModels = probe.Models;
            if (!probe.Ok && _aiModels.Count > 0)
            {
                AiFillModelButton.Content = "模型名改用「" + _aiModels[0] + "」";
                AiFillModelButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            AiStatusText.Text = "已经取消，没有等到答复。";
        }
        catch (Exception ex)
        {
            StarLog.Error("[AI] 连接自检失败", ex);
            AiStatusText.Text = "自检没能完成：" + ex.Message;
        }
        finally
        {
            _aiBusy = false;
            _aiProbeCts = null;
            AiProbeButton.Content = "测试连接";
            cts.Dispose();
        }
    }

    private void AiFillModel_Click(object sender, RoutedEventArgs e)
    {
        if (_aiModels.Count == 0) return;
        AiModelBox.Text = _aiModels[0];       // TextChanged 会顺手保存并刷新状态
    }
}
