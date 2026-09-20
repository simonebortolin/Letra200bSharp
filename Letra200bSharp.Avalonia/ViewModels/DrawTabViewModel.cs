using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;
using SkiaSharp;

namespace Letra200bSharp.Avalonia.ViewModels;

/// <summary>
/// One canvas, two uses: draw a whole label freehand and print/save it directly (like the Image
/// tab, but drawn instead of imported), or draw a small reusable icon, trim it and save it into a
/// personal <see cref="Symbols"/> library to print again later - see <see cref="SaveAsSymbol"/>
/// and <see cref="LoadSymbol"/>. There's no compositor that inserts a saved symbol inline with
/// text yet; loading a symbol replaces the canvas so it can be reprinted or built on.
/// </summary>
/// <remarks>
/// The canvas model (<see cref="_pixels"/>) is always exactly <see cref="CanvasHeightDots"/> rows
/// - the printer's printable head axis - by <see cref="CanvasWidthDots"/> columns (the feed
/// axis, resizable), so it's already what <see cref="LetraHelper.CreateJob(byte[], bool, bool)"/>'s
/// <c>preRendered</c> path expects: no rotation or rescale needed anywhere in this class, just a
/// 1-bit-per-pixel PNG encode. <see cref="CanvasBitmap"/> is a separate, purely cosmetic
/// <see cref="WriteableBitmap"/> - the same model redrawn at <see cref="Zoom"/>/<see cref="HorizontalZoom"/>
/// screen pixels per dot (non-uniform - see <see cref="RebuildCanvasBitmap"/> for why) so editing
/// individual dots is actually possible and WYSIWYG - rebuilt from the model after every stroke
/// rather than being the model itself.
/// </remarks>
public partial class DrawTabViewModel : ViewModelBase
{
    /// <summary>Printer dots available on the head axis - matches every other tab's <c>preRendered</c> content (30, padded to 32 by the shared pipeline).</summary>
    public const int CanvasHeightDots = 30;

    private const int DefaultCanvasWidthDots = 160;
    private const int MinCanvasWidthDots = 8;
    private const int MaxCanvasWidthDots = 800;

    /// <summary>How many previous strokes <see cref="Undo"/> can step back through.</summary>
    private const int MaxUndoDepth = 30;

    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly SymbolLibraryService _symbolLibrary;
    private readonly Action<LetraPrintResult> _recordStats;

    /// <summary>[y, x] - true is a printed (black) dot. Always <see cref="CanvasHeightDots"/> rows by <see cref="CanvasWidthDots"/> columns.</summary>
    private bool[,] _pixels = new bool[CanvasHeightDots, DefaultCanvasWidthDots];

    private readonly List<bool[,]> _undoStack = new();
    private bool[,]? _strokeStartSnapshot;
    private int? _lastPaintX, _lastPaintY;

    public enum DrawTool
    {
        Pen,
        Eraser
    }

    public ObservableCollection<int> ZoomLevels { get; } = new(new[] { 4, 6, 8, 10, 14, 18, 24 });

    /// <summary>
    /// Alternates two brush families - see <see cref="StampBrush"/>: a whole number N stamps a
    /// physically square N-by-2N (head-by-feed) block, matching every other physically-square
    /// thing in this app; a half step N.5 instead stamps a literal (N+1)-by-(N+1) block - not
    /// physically square, but useful when you want the smallest possible mark on one axis
    /// specifically (e.g. a single feed-axis-wide line) rather than the smallest physically square one.
    /// </summary>
    public ObservableCollection<decimal> BrushSizes { get; } =
        new(new[] { 0.5m, 1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m, 4.5m, 5m, 5.5m, 6m });

    public ObservableCollection<SavedSymbol> Symbols => _symbolLibrary.Symbols;

    public bool HasSymbols => Symbols.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPenSelected))]
    [NotifyPropertyChangedFor(nameof(IsEraserSelected))]
    public partial string SelectedTool { get; set; } = nameof(DrawTool.Pen);

    /// <summary>Whether the Pen tool button should show as pressed - see the two-button toggle in <c>DrawTabView.axaml</c> (a ComboBox with two entries isn't comfortably tappable on mobile).</summary>
    public bool IsPenSelected => SelectedTool == nameof(DrawTool.Pen);

    /// <summary>See <see cref="IsPenSelected"/>.</summary>
    public bool IsEraserSelected => SelectedTool == nameof(DrawTool.Eraser);

    [RelayCommand]
    private void SelectTool(string tool) => SelectedTool = tool;

    [ObservableProperty]
    public partial decimal BrushSize { get; set; } = 1m;

    /// <summary>
    /// Screen pixels for one head-axis (vertical) printer dot - drives the canvas height
    /// directly, same as before the 2:1 correction existed, since the head axis (30 dots, fixed)
    /// is what should set the canvas's on-screen size as you zoom in and out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HorizontalZoom))]
    public partial int Zoom { get; set; } = 10;

    /// <summary>
    /// Screen pixels for one feed-axis (horizontal) printer dot - always half <see cref="Zoom"/>,
    /// since a feed-axis dot is physically half the size of a head-axis one (see
    /// <c>LetraHelper.FeedAxisPixelsPerMm</c>'s remarks). The correction lands here rather than by
    /// inflating <see cref="Zoom"/>: the head axis is a fixed 30 dots, so keeping it in direct
    /// control of canvas height keeps that height exactly what it was before this 2:1 fix: the
    /// (already resizable, already-scrolling) feed axis is what narrows instead. Without this
    /// correction at all, a shape that looks square while drawing would actually print as a 2:1
    /// tall rectangle - see <see cref="RebuildCanvasBitmap"/> and the pointer mapping in
    /// <c>DrawTabView.axaml.cs</c>. All <see cref="ZoomLevels"/> are even so this is always exact,
    /// never a fractional screen pixel.
    /// </summary>
    public int HorizontalZoom => Zoom / 2;

    [ObservableProperty]
    public partial bool ShowGrid { get; set; }

    [ObservableProperty]
    public partial int CanvasWidthDots { get; set; } = DefaultCanvasWidthDots;

    [ObservableProperty]
    public partial WriteableBitmap? CanvasBitmap { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    [ObservableProperty]
    public partial string SymbolName { get; set; } = "";

    public bool CanUndo => _undoStack.Count > 0;

    private DrawTool CurrentTool => Enum.Parse<DrawTool>(SelectedTool);

    public DrawTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, SymbolLibraryService symbolLibrary, Action<LetraPrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _symbolLibrary = symbolLibrary;
        _recordStats = recordStats;

        Symbols.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSymbols));

        RebuildCanvasBitmap();
    }

    partial void OnZoomChanged(int value) => RebuildCanvasBitmap();

    partial void OnShowGridChanged(bool value) => RebuildCanvasBitmap();

    partial void OnCanvasWidthDotsChanged(int value)
    {
        value = Math.Clamp(value, MinCanvasWidthDots, MaxCanvasWidthDots);
        if (_pixels.GetLength(1) == value)
        {
            return;
        }

        var resized = new bool[CanvasHeightDots, value];
        int copyWidth = Math.Min(_pixels.GetLength(1), value);
        for (int y = 0; y < CanvasHeightDots; y++)
        {
            for (int x = 0; x < copyWidth; x++)
            {
                resized[y, x] = _pixels[y, x];
            }
        }

        _pixels = resized;
        _undoStack.Clear();
        RebuildCanvasBitmap();
    }

    /// <summary>Restores a previously drawn canvas (see <see cref="Services.HistoryEntry.DrawParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(DrawHistoryParams parameters)
    {
        LoadPixelsFrom(parameters.Png);
        PreviewCommand.Execute(null);
    }

    /// <summary>Loads a saved symbol onto the canvas, replacing whatever was there - the symbol can then be reprinted as-is or edited into something new.</summary>
    [RelayCommand]
    private void LoadSymbol(SavedSymbol symbol)
    {
        LoadPixelsFrom(symbol.PixelPng);
        SymbolName = symbol.Name;
        PreviewCommand.Execute(null);
    }

    private void LoadPixelsFrom(byte[] png)
    {
        using var decoded = SKBitmap.Decode(png);
        int width = Math.Clamp(decoded.Width, MinCanvasWidthDots, MaxCanvasWidthDots);
        var pixels = new bool[CanvasHeightDots, width];
        for (int y = 0; y < CanvasHeightDots && y < decoded.Height; y++)
        {
            for (int x = 0; x < width && x < decoded.Width; x++)
            {
                pixels[y, x] = decoded.GetPixel(x, y).Red < 128;
            }
        }

        _pixels = pixels;
        _undoStack.Clear();
        CanvasWidthDots = width; // no-op resize if already equal (OnCanvasWidthDotsChanged sees the array already matches) - RebuildCanvasBitmap below covers that case either way.
        RebuildCanvasBitmap();
    }

    /// <summary>Snapshots the canvas before a new pointer-down stroke starts, so <see cref="Undo"/> can step back to it.</summary>
    public void BeginStroke()
    {
        _strokeStartSnapshot = (bool[,])_pixels.Clone();
        _lastPaintX = null;
        _lastPaintY = null;
    }

    /// <summary>Commits the snapshot taken by <see cref="BeginStroke"/> onto the undo stack once the stroke actually changed something.</summary>
    public void EndStroke()
    {
        if (_strokeStartSnapshot == null)
        {
            return;
        }

        if (!PixelsEqual(_strokeStartSnapshot, _pixels))
        {
            _undoStack.Add(_strokeStartSnapshot);
            while (_undoStack.Count > MaxUndoDepth)
            {
                _undoStack.RemoveAt(0);
            }
            OnPropertyChanged(nameof(CanUndo));
        }

        _strokeStartSnapshot = null;
        _lastPaintX = null;
        _lastPaintY = null;
    }

    private static bool PixelsEqual(bool[,] a, bool[,] b)
    {
        if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1))
        {
            return false;
        }

        for (int y = 0; y < a.GetLength(0); y++)
        {
            for (int x = 0; x < a.GetLength(1); x++)
            {
                if (a[y, x] != b[y, x])
                {
                    return false;
                }
            }
        }

        return true;
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _pixels = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        OnPropertyChanged(nameof(CanUndo));
        RebuildCanvasBitmap();
    }

    [RelayCommand]
    private void Clear()
    {
        BeginStroke();
        Array.Clear(_pixels);
        EndStroke();
        RebuildCanvasBitmap();
    }

    /// <summary>
    /// Paints (or erases, per <see cref="SelectedTool"/>) a <see cref="BrushSize"/> stamp - see
    /// <see cref="StampBrush"/> for why it isn't a literal <see cref="BrushSize"/>×<see cref="BrushSize"/>
    /// square in dot counts - at the given model (printer-dot) coordinates, stepping along the
    /// line from the last painted point first so a fast drag doesn't leave gaps between samples.
    /// </summary>
    public void PaintAt(int modelX, int modelY)
    {
        if (_lastPaintX is int lastX && _lastPaintY is int lastY)
        {
            foreach (var (x, y) in WalkLine(lastX, lastY, modelX, modelY))
            {
                StampBrush(x, y);
            }
        }
        else
        {
            StampBrush(modelX, modelY);
        }

        _lastPaintX = modelX;
        _lastPaintY = modelY;
        RebuildCanvasBitmap();
    }

    /// <summary>
    /// Stamps a block of dots sized by <see cref="BrushSize"/> - see <see cref="BrushSizes"/> for
    /// the two families: a whole <see cref="BrushSize"/> N is N-by-2N (head-by-feed), physically
    /// square because a feed-axis (X, width) dot is half the size of a head-axis (Y, height) dot
    /// (the same 2:1 fact <see cref="RebuildCanvasBitmap"/> corrects for on screen); a half step
    /// N.5 is instead a literal (N+1)-by-(N+1) block in dot counts.
    /// </summary>
    private void StampBrush(int centerX, int centerY)
    {
        bool value = CurrentTool == DrawTool.Pen;
        bool isHalfStep = BrushSize != Math.Floor(BrushSize);
        int brushHeight = (int)Math.Ceiling(BrushSize);
        int brushWidth = isHalfStep ? brushHeight : brushHeight * 2;
        int halfHeight = brushHeight / 2;
        int halfWidth = brushWidth / 2;
        int width = _pixels.GetLength(1);
        for (int dy = -halfHeight; dy < brushHeight - halfHeight; dy++)
        {
            int y = centerY + dy;
            if (y < 0 || y >= CanvasHeightDots)
            {
                continue;
            }

            for (int dx = -halfWidth; dx < brushWidth - halfWidth; dx++)
            {
                int x = centerX + dx;
                if (x < 0 || x >= width)
                {
                    continue;
                }

                _pixels[y, x] = value;
            }
        }
    }

    /// <summary>Bresenham stepping between two model points, so a brush stroke stays continuous even when pointer-move samples land several dots apart.</summary>
    private static IEnumerable<(int X, int Y)> WalkLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>Mid-gray used for the optional pixel grid overlay (see <see cref="ShowGrid"/>) - visible against both black and white cells without hiding either.</summary>
    private const byte GridLineShade = 170;

    /// <summary>
    /// Redraws <see cref="CanvasBitmap"/> from <see cref="_pixels"/>, nearest-neighbor, at
    /// <see cref="HorizontalZoom"/> screen pixels per feed-axis (horizontal) dot and
    /// <see cref="Zoom"/> per head-axis (vertical) dot - deliberately <em>not</em> a uniform
    /// block like <see cref="LetraHelper.PreviewImage(byte[], bool, bool)"/>'s upscale, because on
    /// the printer itself those two axes aren't the same physical size: a shape that looks square
    /// here has to actually be twice as wide (in dots) as it is tall to print square (see
    /// <see cref="HorizontalZoom"/>) - this is what makes the live canvas WYSIWYG against the
    /// (already correctly scaled) preview box below it. When <see cref="ShowGrid"/> is set, the
    /// right/bottom edge of every cell is painted <see cref="GridLineShade"/> so individual dots
    /// stay easy to target while drawing.
    /// </summary>
    private void RebuildCanvasBitmap()
    {
        int hZoom = HorizontalZoom;
        int vZoom = Zoom;
        int height = CanvasHeightDots;
        int width = _pixels.GetLength(1);
        int devWidth = width * hZoom;
        int devHeight = height * vZoom;
        bool showGrid = ShowGrid;

        var bitmap = new WriteableBitmap(new PixelSize(devWidth, devHeight), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
        using (var frameBuffer = bitmap.Lock())
        {
            var contentRow = new byte[devWidth * 4];
            byte[]? gridRow = null;
            if (showGrid)
            {
                gridRow = new byte[devWidth * 4];
                for (int i = 0; i < gridRow.Length; i += 4)
                {
                    gridRow[i] = GridLineShade;
                    gridRow[i + 1] = GridLineShade;
                    gridRow[i + 2] = GridLineShade;
                    gridRow[i + 3] = 255;
                }
            }

            for (int y = 0; y < height; y++)
            {
                int pos = 0;
                for (int x = 0; x < width; x++)
                {
                    byte shade = _pixels[y, x] ? (byte)0 : (byte)255;
                    for (int zx = 0; zx < hZoom; zx++)
                    {
                        byte cell = showGrid && zx == hZoom - 1 ? GridLineShade : shade;
                        contentRow[pos++] = cell;
                        contentRow[pos++] = cell;
                        contentRow[pos++] = cell;
                        contentRow[pos++] = 255;
                    }
                }

                for (int zy = 0; zy < vZoom; zy++)
                {
                    bool gridRowHere = showGrid && zy == vZoom - 1;
                    var source = gridRowHere ? gridRow! : contentRow;
                    var destination = frameBuffer.Address + (y * vZoom + zy) * frameBuffer.RowBytes;
                    Marshal.Copy(source, 0, destination, source.Length);
                }
            }
        }

        var previous = CanvasBitmap;
        CanvasBitmap = bitmap;
        previous?.Dispose();
    }

    private bool HasInk()
    {
        foreach (bool pixel in _pixels)
        {
            if (pixel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Encodes <paramref name="pixels"/> as a pixel-exact, 1-bit-per-pixel PNG - the print-ready form handed to <see cref="LetraHelper"/> and to <see cref="SymbolLibraryService"/>.</summary>
    private static byte[] EncodePixelsPng(bool[,] pixels)
    {
        int height = pixels.GetLength(0);
        int width = pixels.GetLength(1);
        using var bitmap = new SKBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, pixels[y, x] ? SKColors.Black : SKColors.White);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>Crops the canvas down to the columns that actually have ink, keeping the full head-axis height - the shape a saved symbol should have so it doesn't carry blank margin baked in.</summary>
    private bool[,] TrimToContent()
    {
        int width = _pixels.GetLength(1);
        int minX = width, maxX = -1;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < CanvasHeightDots; y++)
            {
                if (_pixels[y, x])
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    break;
                }
            }
        }

        if (maxX < 0)
        {
            return _pixels;
        }

        int trimmedWidth = maxX - minX + 1;
        var trimmed = new bool[CanvasHeightDots, trimmedWidth];
        for (int y = 0; y < CanvasHeightDots; y++)
        {
            for (int x = 0; x < trimmedWidth; x++)
            {
                trimmed[y, x] = _pixels[y, minX + x];
            }
        }

        return trimmed;
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var modelPng = EncodePixelsPng(_pixels);
        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = LetraHelper.PreviewImage(modelPng, noCut: false, preRendered: true);
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
        if (!HasInk())
        {
            _reportStatus(Strings.DrawTab_NothingDrawn, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var modelPng = EncodePixelsPng(_pixels);

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(modelPng, noCut: false, preRendered: true));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(modelPng, printed: true);
                }
                catch
                {
                    // A history-recording failure must never look like the print itself failed.
                }
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
    /// Saves the current canvas to history without printing it - lets a design be kept/reused
    /// (see <see cref="Services.HistoryEntry.Printed"/>) even without a printer in reach. Unlike
    /// the best-effort recording after a successful print, a failure here is the whole point of
    /// the action, so it's reported to the user instead of swallowed.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (!HasInk())
        {
            _reportStatus(Strings.DrawTab_NothingDrawn, true);
            return;
        }

        try
        {
            RecordHistory(EncodePixelsPng(_pixels), printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    private void RecordHistory(byte[] modelPng, bool printed)
    {
        var thumbnail = LetraHelper.PreviewImage(modelPng, noCut: false, preRendered: true);
        var parameters = new DrawHistoryParams(modelPng, CanvasWidthDots);
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Draw, Strings.DrawTab_DefaultHistoryLabel, thumbnail, DrawParams: parameters, Printed: printed));
    }

    /// <summary>Trims the canvas to its ink (see <see cref="TrimToContent"/>) and adds it to the personal symbol library, for reuse via <see cref="LoadSymbol"/> or <see cref="PrintSymbolAsync"/> without needing to redraw it.</summary>
    [RelayCommand]
    private void SaveAsSymbol()
    {
        if (!HasInk())
        {
            _reportStatus(Strings.DrawTab_NothingDrawn, true);
            return;
        }

        try
        {
            var trimmed = TrimToContent();
            var png = EncodePixelsPng(trimmed);
            var name = string.IsNullOrWhiteSpace(SymbolName) ? string.Format(Strings.DrawTab_DefaultSymbolNameFormat, DateTime.Now) : SymbolName.Trim();
            _symbolLibrary.Add(new SavedSymbol(Guid.NewGuid(), name, DateTimeOffset.Now, trimmed.GetLength(1), png));
            _reportStatus(Strings.DrawTab_SymbolSaved, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    [RelayCommand]
    private void DeleteSymbol(SavedSymbol symbol) => _symbolLibrary.Remove(symbol.Id);

    /// <summary>Prints a saved symbol directly, without loading it onto (and so replacing) the canvas.</summary>
    [RelayCommand]
    private async Task PrintSymbolAsync(SavedSymbol symbol)
    {
        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(symbol.PixelPng, noCut: false, preRendered: true));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);
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
}
