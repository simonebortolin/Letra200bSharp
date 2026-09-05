using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Letra200bSharp.Avalonia.Resources;

namespace Letra200bSharp.Avalonia.ViewModels;

/// <summary>One clickable row in the "Used libraries" or "Acknowledgements" list.</summary>
/// <param name="Name">The library/project's display name.</param>
/// <param name="Url">Where <see cref="AboutTabViewModel.OpenLinkCommand"/> sends the user.</param>
/// <param name="Description">One-line blurb of what it's used for / credited for.</param>
public record AboutLinkItem(string Name, string Url, string Description);

public partial class AboutTabViewModel : ViewModelBase
{
    /// <summary>
    /// Wired up by the view (needs a TopLevel to launch the system browser), since the
    /// ViewModel itself has no platform/visual-tree access - same reasoning as
    /// <see cref="ImageTabViewModel.PickFileAsync"/>.
    /// </summary>
    public Func<string, Task>? OpenLinkAsync { get; set; }

    public string AppName => "Letra200bSharp";

    public string AppTagline => Strings.AboutTab_Tagline;

    /// <summary>
    /// The assembly's version, or "dev" if it was built without one (e.g. a local build with
    /// no version stamped into the csproj/CI pipeline) - there's no reliable version to show
    /// either way, so this just avoids printing a misleading "0.0.0.0".
    /// </summary>
    public string AppVersion { get; } = FormatVersion();

    public string RepositoryUrl => "https://github.com/simonebortolin/Letra200bSharp";

    public string LicenseUrl => "https://github.com/simonebortolin/Letra200bSharp/blob/main/LICENSE";

    public IReadOnlyList<AboutLinkItem> Libraries { get; } =
    [
        new("SkiaSharp", "https://github.com/mono/SkiaSharp", "Image processing - resizing/thresholding source images, rendering text and barcodes, generating label previews"),
        new("InTheHand.BluetoothLE", "https://github.com/inthehand/32feet", "Cross-platform Bluetooth LE scanning and GATT communication with the printer"),
        new("ZXing.Net", "https://github.com/micjahn/ZXing.Net", "Encoding barcode data (Code128, Code39, Codabar, ITF, EAN/UPC, ...) into the bit matrix printed on the label"),
        new("Avalonia", "https://avaloniaui.net/", "Cross-platform UI framework behind the desktop and Android apps"),
        new("SukiUI", "https://github.com/kikipoulet/SukiUI", "The app's visual theme - light/dark styling, toast notifications, busy overlays"),
        new("CommunityToolkit.Mvvm", "https://learn.microsoft.com/en-gb/dotnet/communitytoolkit/", "MVVM boilerplate - source-generated observable properties and relay commands"),
        new("CommandLineParser", "https://github.com/commandlineparser/commandline", "Parsing arguments in the desktop app's headless CLI mode"),
        new("CsWin32", "https://github.com/microsoft/cswin32", "Source-generated, type-safe P/Invoke on Windows"),
    ];

    public IReadOnlyList<AboutLinkItem> Acknowledgements { get; } =
    [
        new("letra200bsharp", "https://github.com/brz/letra200bsharp", "The original C# project this repository is forked from"),
        new("lt200b", "https://github.com/alexhorn/lt200b", "Python reference implementation the printing protocol is based on"),
        new("dymo-bluetooth", "https://github.com/ysfchn/dymo-bluetooth", "Reverse-engineered documentation of the LetraTag 200B's BLE service/characteristic UUIDs and status codes"),
        new("homeassistant_letratag", "https://github.com/renaudallard/homeassistant_letratag", "Home Assistant integration with its own BLE protocol reference (chunk framing, sequence numbering, manufacturer advertisement data)"),
        new("thermal-label", "https://thermal-label.github.io/letratag/protocol/letratag-bt", "Independent BLE protocol reference (opcodes, raster packing, advertisement data) used to cross-check this project's framing"),
    ];

    private static string FormatVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version == null ? "dev" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    [RelayCommand]
    private async Task OpenLink(string url)
    {
        if (OpenLinkAsync != null)
        {
            await OpenLinkAsync(url);
        }
    }
}
