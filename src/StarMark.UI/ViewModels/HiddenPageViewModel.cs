#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarMark.Abstractions;
using StarMark.Abstractions.I18n;

namespace StarMark.UI.ViewModels;

/// <summary>
/// 隐藏条目管理页面 ViewModel。对应浏览器扩展 hidden tab。
/// </summary>
public partial class HiddenPageViewModel : ObservableObject
{
    private readonly IItemRepository _repository;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private LocMessage? _emptyHint;
    [ObservableProperty] private bool _hasItems;

    public ObservableCollection<ItemCardViewModel> HiddenItems { get; } = new();

    public HiddenPageViewModel(IItemRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        HiddenItems.Clear();
        try
        {
            var items = await _repository.GetHiddenAsync(CancellationToken.None);
            foreach (var item in items)
                HiddenItems.Add(new ItemCardViewModel(item));
            HasItems = HiddenItems.Count > 0;
            EmptyHint = HiddenItems.Count == 0 ? LocMessage.Of("page.hidden.empty") : null;
        }
        catch (Exception ex)
        {
            // HasItems 必须一起复位：那一行的可见性绑的就是它，留着 true 这句"加载失败"就藏在屏幕外（批次 IJ 同一条口径）。
            HasItems = false;
            EmptyHint = LocMessage.Of("common.loadFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(ItemCardViewModel item)
    {
        try
        {
            // 落库成功才从列表里摘掉。旧写法是"先摘再吞异常"：失败时界面当场少一条，
            // 用户以为还原好了，下次进这页它又回来——比报错更难查的是"看起来成功了"。
            // 0 行（那一行已经不在了）走同一条口径：不摘行，并把原因当面说出来，不靠这页没有的状态行（P-40）。
            if (!await _repository.SetHiddenAsync(item.Id, false, CancellationToken.None))
            {
                var word = LocMessage.Of("card.restore.word");
                App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.RowGone(word));
                return;
            }
            HiddenItems.Remove(item);
            HasItems = HiddenItems.Count > 0;
            EmptyHint = HiddenItems.Count == 0 ? LocMessage.Of("page.hidden.empty") : null;
        }
        catch (Exception ex)
        {
            // 抛异常＝库里那一行没动 ⇒ 行⛔ 从列表里摘（上面那句"落库成功才摘"是同一条判据的另一半）。
            // 可见原因走与 0 行那一支<b>同一颗出口</b>（右下角提示条）：P-64 要的是"原因得当面说出来"，
            // ⛔ 再给这一页长一行自定义状态位——那会变成同一颗失败在同一个应用里有两副面孔。
            var word = LocMessage.Of("card.restore.word");
            App.MainWindow?.ShowError(StateWriteNotice.Title(word), StateWriteNotice.WriteFailed(word, ex.Message));
            StarLog.Error($"还原隐藏条目失败 (id={item.Id})", ex);
        }
    }
}
