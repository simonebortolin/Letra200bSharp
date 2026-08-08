using Avalonia.Controls;
using Avalonia.Platform.Storage;
using letra200bsharp.Avalonia.ViewModels;

namespace letra200bsharp.Avalonia.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => WireFilePicker();
    }

    private void WireFilePicker()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.Image.PickFileAsync = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } storageProvider)
            {
                return null;
            }

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select an image",
                AllowMultiple = false,
                // Every raster format SkiaSharp's SKBitmap.Decode can read (it auto-detects
                // from content, so LetraHelper itself needs no changes for any of these).
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images")
                    {
                        Patterns = new[]
                        {
                            "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp",
                            "*.ico", "*.wbmp", "*.pkm", "*.ktx", "*.astc", "*.dng"
                        }
                    }
                }
            });

            if (files.Count == 0)
            {
                return null;
            }

            // Read the bytes here, through the picked IStorageFile's own stream, rather
            // than handing back a path: on Android the picker returns a content:// URI
            // that System.IO.File can't open at all, so a path-based read silently fails
            // there even though the same code works fine on desktop.
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return (file.Name, memoryStream.ToArray());
        };
    }
}
