using Avalonia.Controls;
using Avalonia.Input;
using Letra200bSharp.Avalonia.ViewModels;

namespace Letra200bSharp.Avalonia.Views;

public partial class DrawTabView : UserControl
{
    private bool _isDrawing;

    public DrawTabView()
    {
        InitializeComponent();
        CanvasImage.PointerPressed += OnCanvasPointerPressed;
        CanvasImage.PointerMoved += OnCanvasPointerMoved;
        CanvasImage.PointerReleased += OnCanvasPointerReleased;
        CanvasImage.PointerCaptureLost += (_, _) => EndStroke();
    }

    private DrawTabViewModel? ViewModel => DataContext as DrawTabViewModel;

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } viewModel || !e.GetCurrentPoint(CanvasImage).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDrawing = true;
        e.Pointer.Capture(CanvasImage);
        viewModel.BeginStroke();
        PaintFromPointer(viewModel, e);
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDrawing || ViewModel is not { } viewModel)
        {
            return;
        }

        PaintFromPointer(viewModel, e);
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e) => EndStroke();

    private void EndStroke()
    {
        if (!_isDrawing)
        {
            return;
        }

        _isDrawing = false;
        ViewModel?.EndStroke();
    }

    /// <summary>
    /// Maps the pointer's position on <see cref="CanvasImage"/> (screen pixels) down to model
    /// (printer-dot) coordinates and paints there. X divides out
    /// <see cref="DrawTabViewModel.HorizontalZoom"/> (feed-axis dot size, half as many screen
    /// pixels) and Y divides out <see cref="DrawTabViewModel.Zoom"/> (head-axis dot size) -
    /// matching the non-uniform scale <c>RebuildCanvasBitmap</c> actually draws the bitmap at.
    /// </summary>
    private void PaintFromPointer(DrawTabViewModel viewModel, PointerEventArgs e)
    {
        var position = e.GetPosition(CanvasImage);
        int modelX = (int)Math.Floor(position.X / viewModel.HorizontalZoom);
        int modelY = (int)Math.Floor(position.Y / viewModel.Zoom);
        viewModel.PaintAt(modelX, modelY);
    }
}
