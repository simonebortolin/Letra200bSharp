using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Letra200bSharp.Avalonia.ViewModels;

namespace Letra200bSharp.Avalonia.Views;

public partial class DinRailTabView : UserControl
{
    private static readonly DataFormat<DinRailRow> RowDragFormat = DataFormat.CreateInProcessFormat<DinRailRow>("application/x-letra200bsharp-dinrailrow");

    public DinRailTabView()
    {
        InitializeComponent();
        RowsList.AddHandler(DragDrop.DragOverEvent, OnRowDragOver);
        RowsList.AddHandler(DragDrop.DropEvent, OnRowDrop);
    }

    /// <summary>Starts a drag from the row's grip handle, carrying the row itself so <see cref="OnRowDrop"/> knows what to move.</summary>
    private async void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DinRailRow row })
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RowDragFormat, row));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(RowDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>Reorders <see cref="DinRailTabViewModel.Rows"/> to move the dragged row to wherever it was dropped, identified by walking up from the drop point to its containing <see cref="ListBoxItem"/>.</summary>
    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        var draggedRow = e.DataTransfer.TryGetValue(RowDragFormat);
        if (draggedRow == null)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not { DataContext: DinRailRow targetRow })
        {
            return;
        }

        if (DataContext is DinRailTabViewModel viewModel && !ReferenceEquals(draggedRow, targetRow))
        {
            viewModel.MoveRow(draggedRow, viewModel.Rows.IndexOf(targetRow));
        }
    }
}
