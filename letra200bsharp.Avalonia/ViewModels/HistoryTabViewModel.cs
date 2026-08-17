using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class HistoryTabViewModel : ViewModelBase
{
    private readonly PrintHistoryService _historyService;
    private readonly TextTabViewModel _text;
    private readonly BarcodeTabViewModel _barcode;
    private readonly DinRailTabViewModel _dinRail;
    private readonly Action<int> _selectTab;

    public ObservableCollection<HistoryEntry> Entries => _historyService.Entries;

    public bool HasEntries => Entries.Count > 0;

    public HistoryTabViewModel(PrintHistoryService historyService, TextTabViewModel text, BarcodeTabViewModel barcode, DinRailTabViewModel dinRail, Action<int> selectTab)
    {
        _historyService = historyService;
        _text = text;
        _barcode = barcode;
        _dinRail = dinRail;
        _selectTab = selectTab;

        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEntries));
    }

    /// <summary>
    /// Restores the entry's parameters into the tab it was printed from and switches to it -
    /// printing itself is still left as an explicit action for the user, same as everywhere
    /// else in the app.
    /// </summary>
    [RelayCommand]
    private void Reprint(HistoryEntry entry)
    {
        switch (entry.Kind)
        {
            case HistoryKind.Text when entry.TextParams != null:
                _text.LoadFrom(entry.TextParams);
                _selectTab(MainViewModel.TextTabIndex);
                break;
            case HistoryKind.Barcode when entry.BarcodeParams != null:
                _barcode.LoadFrom(entry.BarcodeParams);
                _selectTab(MainViewModel.BarcodeTabIndex);
                break;
            case HistoryKind.DinRail when entry.DinRailParams != null:
                _dinRail.LoadFrom(entry.DinRailParams);
                _selectTab(MainViewModel.DinRailTabIndex);
                break;
        }
    }

    [RelayCommand]
    private void Delete(HistoryEntry entry)
    {
        _historyService.Remove(entry.Id);
    }
}
