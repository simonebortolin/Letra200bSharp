using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Letra200bSharp.Avalonia.Services;
using Letra200bSharp.Avalonia.ViewModels;

namespace Letra200bSharp.Avalonia.Views;

public partial class ComposeTabView : UserControl
{
    private static readonly DataFormat<ComposeElement> ElementDragFormat = DataFormat.CreateInProcessFormat<ComposeElement>("application/x-letra200bsharp-composeelement");

    public ComposeTabView()
    {
        InitializeComponent();
        ElementsList.AddHandler(DragDrop.DragOverEvent, OnElementDragOver);
        ElementsList.AddHandler(DragDrop.DropEvent, OnElementDrop);
    }

    /// <summary>Starts a drag from the element's grip handle, carrying the element itself so <see cref="OnElementDrop"/> knows what to move.</summary>
    private async void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ComposeElement element })
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(ElementDragFormat, element));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnElementDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(ElementDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>Reorders <see cref="ComposeTabViewModel.Elements"/> to move the dragged element to wherever it was dropped, identified by walking up from the drop point to its containing <see cref="ListBoxItem"/>.</summary>
    private void OnElementDrop(object? sender, DragEventArgs e)
    {
        var draggedElement = e.DataTransfer.TryGetValue(ElementDragFormat);
        if (draggedElement == null)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not { DataContext: ComposeElement targetElement })
        {
            return;
        }

        if (DataContext is ComposeTabViewModel viewModel && !ReferenceEquals(draggedElement, targetElement))
        {
            viewModel.MoveElement(draggedElement, viewModel.Elements.IndexOf(targetElement));
        }
    }
}
