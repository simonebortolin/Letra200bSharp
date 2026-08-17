using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;
using SkiaSharp;

namespace Letra200bSharp.Avalonia.ViewModels;

/// <summary>One row of the DIN Rail strip being built - a segment of text and how many 18mm DIN modules wide it should be.</summary>
public sealed partial class DinRailRow : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial decimal Modules { get; set; } = 1.0m;

    /// <summary>
    /// Set by <see cref="DinRailTabViewModel"/> when this row's text would need heavy shrinking
    /// to fit its module count (see <see cref="LetraHelper.DinRailRequiredScale"/>) - a hint that
    /// the printed text will likely be too small to read, so the user knows to either shorten it
    /// or add more modules instead of only discovering it after printing.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTooSmallToReadWell { get; set; }
}

/// <summary>
/// Builds a list of text/module-count rows and prints them as one continuous DIN rail strip
/// (see <see cref="LetraHelper.CreateDinRailRowJob"/>) - unlike <see cref="TextTabViewModel"/>,
/// each row's text is always auto-fit (auto-wrapped to 2 lines if that helps it fit better,
/// then scaled uniformly - never stretched/squeezed non-uniformly) to its own segment, with no
/// manual height/stretch controls; see <see cref="LetraHelper.DinRailSizing"/> for the two ways
/// that uniform scale can be chosen.
/// </summary>
public partial class DinRailTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly Action<LetraPrintResult> _recordStats;

    public ObservableCollection<string> FontFamilies { get; }

    /// <summary>Same as <see cref="LetraHelper.TextStyle"/> minus <see cref="LetraHelper.TextStyle.Vertical"/> - one-character-per-line doesn't combine sensibly with the automatic word-wrap each segment already does.</summary>
    public ObservableCollection<string> Styles { get; } = new(Enum.GetNames<LetraHelper.TextStyle>().Where(name => name != nameof(LetraHelper.TextStyle.Vertical)));

    public ObservableCollection<string> Aligns { get; } = new(Enum.GetNames<LetraHelper.TextAlign>());
    public ObservableCollection<string> Sizings { get; } = new(Enum.GetNames<LetraHelper.DinRailSizing>());

    public ObservableCollection<DinRailRow> Rows { get; } = new();

    [ObservableProperty]
    public partial string? SelectedFontFamily { get; set; }

    [ObservableProperty]
    public partial string SelectedStyle { get; set; } = nameof(LetraHelper.TextStyle.Normal);

    [ObservableProperty]
    public partial string SelectedAlign { get; set; } = nameof(LetraHelper.TextAlign.Center);

    [ObservableProperty]
    public partial string SelectedSizing { get; set; } = nameof(LetraHelper.DinRailSizing.MaxPerLabel);

    [ObservableProperty]
    public partial bool UpperCase { get; set; }

    [ObservableProperty]
    public partial bool ShowSeparators { get; set; } = true;

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    [ObservableProperty]
    public partial string SizeNoteText { get; set; } = "";

    /// <summary>The running total of DIN modules across every row, shown next to the column header - see <see cref="UpdateSizeNote"/>.</summary>
    [ObservableProperty]
    public partial string TotalModulesText { get; set; } = "";

    /// <summary>Below this required scale (see <see cref="LetraHelper.DinRailRequiredScale"/>), a row is flagged as likely too small to read once printed.</summary>
    private const float LegibilityWarningThreshold = 0.3f;

    public DinRailTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, Action<LetraPrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _recordStats = recordStats;

        var fontFamilies = SKFontManager.Default.FontFamilies.OrderBy(f => f).ToArray();
        FontFamilies = new ObservableCollection<string>(fontFamilies);
        SelectedFontFamily = fontFamilies.Contains("Arial") ? "Arial" : fontFamilies.FirstOrDefault();

        AddRow();
    }

    partial void OnSelectedFontFamilyChanged(string? value) => RecomputeAllWarnings();
    partial void OnSelectedStyleChanged(string value) => RecomputeAllWarnings();
    partial void OnSelectedAlignChanged(string value) => RecomputeAllWarnings();
    partial void OnUpperCaseChanged(bool value) => RecomputeAllWarnings();

    private void RecomputeAllWarnings()
    {
        foreach (var row in Rows)
        {
            RecomputeRowWarning(row);
        }
    }

    private void RecomputeRowWarning(DinRailRow row)
    {
        var fontFamily = SelectedFontFamily ?? "Arial";
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var align = Enum.Parse<LetraHelper.TextAlign>(SelectedAlign);
        float scale = LetraHelper.DinRailRequiredScale(row.Text, fontFamily, style, UpperCase, align, row.Modules, true);
        row.IsTooSmallToReadWell = scale < LegibilityWarningThreshold;
    }

    /// <summary>Restores a previously printed DIN rail strip (see <see cref="Services.HistoryEntry.DinRailParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(DinRailHistoryParams parameters)
    {
        Rows.Clear();
        foreach (var row in parameters.Rows)
        {
            AddRowTracking(new DinRailRow { Text = row.Text, Modules = row.Modules });
        }
        if (Rows.Count == 0)
        {
            AddRow();
        }

        if (parameters.FontFamily != null && FontFamilies.Contains(parameters.FontFamily))
        {
            SelectedFontFamily = parameters.FontFamily;
        }
        SelectedStyle = Styles.Contains(parameters.Style) ? parameters.Style : nameof(LetraHelper.TextStyle.Normal);
        UpperCase = parameters.UpperCase;
        SelectedAlign = parameters.Align;
        SelectedSizing = Sizings.Contains(parameters.Sizing) ? parameters.Sizing : nameof(LetraHelper.DinRailSizing.Uniform);
        ShowSeparators = parameters.ShowSeparators;

        UpdateSizeNote();
        PreviewCommand.Execute(null);
    }

    [RelayCommand]
    private void AddRow() => AddRowTracking(new DinRailRow());

    /// <summary>
    /// Adds <paramref name="row"/> and subscribes to its <see cref="DinRailRow.Text"/>/<see cref="DinRailRow.Modules"/>
    /// changes so <see cref="SizeNoteText"/> and the row's legibility warning (see
    /// <see cref="RecomputeRowWarning"/>) stay accurate as the user edits the list.
    /// </summary>
    private void AddRowTracking(DinRailRow row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DinRailRow.Modules))
            {
                UpdateSizeNote();
            }
            if (e.PropertyName is nameof(DinRailRow.Text) or nameof(DinRailRow.Modules))
            {
                RecomputeRowWarning(row);
            }
        };
        Rows.Add(row);
        UpdateSizeNote();
        RecomputeRowWarning(row);
    }

    /// <summary>Reorders the strip by moving <paramref name="row"/> to <paramref name="newIndex"/> - see <see cref="Views.DinRailTabView"/>'s drag-and-drop handling.</summary>
    public void MoveRow(DinRailRow row, int newIndex)
    {
        int oldIndex = Rows.IndexOf(row);
        if (oldIndex < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Rows.Count - 1);
        if (newIndex != oldIndex)
        {
            Rows.Move(oldIndex, newIndex);
        }
    }

    [RelayCommand]
    private void RemoveRow(DinRailRow row)
    {
        // Always leave at least one row - an empty list has nothing left to edit or print.
        if (Rows.Count <= 1)
        {
            return;
        }

        Rows.Remove(row);
        UpdateSizeNote();
    }

    /// <summary>The "12 mm tall · ~X mm printed"-style caption, summed across every row's own physical segment length, plus the running modules total shown next to the column header.</summary>
    private void UpdateSizeNote()
    {
        float totalMm = Rows.Sum(row => LetraHelper.DinRailLengthMm(row.Modules));
        SizeNoteText = string.Format(Strings.DinRailTab_SizeNoteFormat, totalMm, Rows.Count);
        TotalModulesText = string.Format(Strings.DinRailTab_TotalModulesFormat, Rows.Sum(row => row.Modules));
    }

    private bool HasAnyText => Rows.Any(row => !string.IsNullOrWhiteSpace(row.Text));

    private List<(string Text, decimal Modules)> BuildRowList() => Rows.Select(row => (row.Text, row.Modules)).ToList();

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!HasAnyText)
        {
            var previousBitmap = PreviewBitmap;
            PreviewBitmap = null;
            previousBitmap?.Dispose();
            return;
        }

        var rows = BuildRowList();
        var fontFamily = SelectedFontFamily ?? "Arial";
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var align = Enum.Parse<LetraHelper.TextAlign>(SelectedAlign);
        var sizing = Enum.Parse<LetraHelper.DinRailSizing>(SelectedSizing);
        var upperCase = UpperCase;
        var showSeparators = ShowSeparators;

        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = LetraHelper.PreviewDinRailRowImage(rows, fontFamily, style, upperCase, align, sizing, showSeparators, true);
                using var stream = new MemoryStream(previewBytes);
                return new Bitmap(stream);
            });

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            _reportStatus(string.Format(Strings.Status_UnableToGeneratePreview, ex.Message), true);
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (!HasAnyText)
        {
            _reportStatus(Strings.DinRailTab_NoTextEntered, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var rows = BuildRowList();
        var fontFamily = SelectedFontFamily ?? "Arial";
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var align = Enum.Parse<LetraHelper.TextAlign>(SelectedAlign);
        var sizing = Enum.Parse<LetraHelper.DinRailSizing>(SelectedSizing);
        var upperCase = UpperCase;
        var showSeparators = ShowSeparators;

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateDinRailRowJob(rows, fontFamily, style, upperCase, align, sizing, showSeparators, true));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                RecordHistory(rows, fontFamily, style, upperCase, align, sizing, showSeparators);
            }
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Best-effort: a history-recording failure must never look like the print itself failed,
    /// so this is never allowed to bubble into <see cref="PrintAsync"/>'s own error reporting.
    /// </summary>
    private void RecordHistory(List<(string Text, decimal Modules)> rows, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, LetraHelper.DinRailSizing sizing, bool showSeparators)
    {
        try
        {
            var thumbnail = LetraHelper.PreviewDinRailRowImage(rows, fontFamily, style, upperCase, align, sizing, showSeparators, true);
            var rowParams = rows.Select(row => new DinRailRowParams(row.Text, row.Modules)).ToList();
            var parameters = new DinRailHistoryParams(rowParams, fontFamily, SelectedStyle, upperCase, SelectedAlign, SelectedSizing, showSeparators);
            var summary = $"{rows.Count} label{(rows.Count == 1 ? "" : "s")}: " + string.Join(" | ", rows.Select(row => row.Text));
            _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.DinRail, summary, thumbnail, DinRailParams: parameters));
        }
        catch
        {
            // See summary above.
        }
    }
}
