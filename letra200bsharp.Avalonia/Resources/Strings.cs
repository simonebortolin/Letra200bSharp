using System.Globalization;
using System.Resources;

namespace Letra200bSharp.Avalonia.Resources;

/// <summary>
/// Strongly-typed access to Strings.resx. Hand-written rather than relying on Visual Studio's
/// ResXFileCodeGenerator/PublicResXFileCodeGenerator "Custom Tool": in this CLI-only build
/// environment, `dotnet build` never actually writes a persistent Strings.Designer.cs - the
/// "successes" observed while iterating on this were a design-time build artifact
/// (obj/.../TempPE/Resources.Strings.Designer.cs.dll, used only for XAML tooling) rather than a
/// real generated source file. Whenever a key is added to Strings.resx, add the matching
/// property below by hand too.
/// </summary>
public static class Strings
{
    private static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager("Letra200bSharp.Avalonia.Resources.Strings", typeof(Strings).Assembly);

    public static CultureInfo? Culture { get; set; }

    private static string Get(string name) => ResourceManager.GetString(name, Culture) ?? name;

    public static string Common_Print => Get(nameof(Common_Print));
    public static string Common_Preview => Get(nameof(Common_Preview));
    public static string Common_NoCut => Get(nameof(Common_NoCut));
    public static string Common_AlignLabel => Get(nameof(Common_AlignLabel));

    public static string MainWindow_Title => Get(nameof(MainWindow_Title));

    public static string MainView_DevicesLabel => Get(nameof(MainView_DevicesLabel));
    public static string MainView_RefreshButton => Get(nameof(MainView_RefreshButton));
    public static string MainView_ImageTabHeader => Get(nameof(MainView_ImageTabHeader));
    public static string MainView_TextTabHeader => Get(nameof(MainView_TextTabHeader));
    public static string MainView_BarcodeTabHeader => Get(nameof(MainView_BarcodeTabHeader));
    public static string MainView_DinRailTabHeader => Get(nameof(MainView_DinRailTabHeader));
    public static string MainView_HistoryTabHeader => Get(nameof(MainView_HistoryTabHeader));
    public static string MainView_AboutTabHeader => Get(nameof(MainView_AboutTabHeader));
    public static string MainView_StatsToggleButtonTooltip => Get(nameof(MainView_StatsToggleButtonTooltip));
    public static string MainView_StatsPanelNoDataYet => Get(nameof(MainView_StatsPanelNoDataYet));
    public static string MainView_NoPrinterFound => Get(nameof(MainView_NoPrinterFound));

    public static string Toast_ErrorTitle => Get(nameof(Toast_ErrorTitle));
    public static string Toast_SuccessTitle => Get(nameof(Toast_SuccessTitle));

    public static string Status_UnableToGeneratePreview => Get(nameof(Status_UnableToGeneratePreview));
    public static string Status_NoDeviceSelected => Get(nameof(Status_NoDeviceSelected));

    public static string ImageTab_ImageLabel => Get(nameof(ImageTab_ImageLabel));
    public static string ImageTab_NoFileSelectedPlaceholder => Get(nameof(ImageTab_NoFileSelectedPlaceholder));
    public static string ImageTab_BrowseButton => Get(nameof(ImageTab_BrowseButton));
    public static string ImageTab_PreRenderedCheckbox => Get(nameof(ImageTab_PreRenderedCheckbox));
    public static string ImageTab_NoImageSelected => Get(nameof(ImageTab_NoImageSelected));
    public static string ImageTab_DefaultHistoryLabel => Get(nameof(ImageTab_DefaultHistoryLabel));

    public static string TextTab_Line2Label => Get(nameof(TextTab_Line2Label));
    public static string TextTab_FontLabel => Get(nameof(TextTab_FontLabel));
    public static string TextTab_HeightLabel => Get(nameof(TextTab_HeightLabel));
    public static string TextTab_WidthLabel => Get(nameof(TextTab_WidthLabel));
    public static string TextTab_StyleLabel => Get(nameof(TextTab_StyleLabel));
    public static string TextTab_BoxLabel => Get(nameof(TextTab_BoxLabel));
    public static string TextTab_UppercaseCheckbox => Get(nameof(TextTab_UppercaseCheckbox));
    public static string TextTab_Line1LabelSingle => Get(nameof(TextTab_Line1LabelSingle));
    public static string TextTab_Line1LabelWithLine2 => Get(nameof(TextTab_Line1LabelWithLine2));
    public static string TextTab_NoTextEntered => Get(nameof(TextTab_NoTextEntered));

    public static string DinRailTab_SizeNoteFormat => Get(nameof(DinRailTab_SizeNoteFormat));
    public static string DinRailTab_NoTextEntered => Get(nameof(DinRailTab_NoTextEntered));
    public static string DinRailTab_TextColumnHeader => Get(nameof(DinRailTab_TextColumnHeader));
    public static string DinRailTab_TextPlaceholder => Get(nameof(DinRailTab_TextPlaceholder));
    public static string DinRailTab_ModulesColumnHeader => Get(nameof(DinRailTab_ModulesColumnHeader));
    public static string DinRailTab_TotalModulesFormat => Get(nameof(DinRailTab_TotalModulesFormat));
    public static string DinRailTab_AddRowButton => Get(nameof(DinRailTab_AddRowButton));
    public static string DinRailTab_RemoveRowTooltip => Get(nameof(DinRailTab_RemoveRowTooltip));
    public static string DinRailTab_DragHandleTooltip => Get(nameof(DinRailTab_DragHandleTooltip));
    public static string DinRailTab_SeparatorsCheckbox => Get(nameof(DinRailTab_SeparatorsCheckbox));
    public static string DinRailTab_SizingLabel => Get(nameof(DinRailTab_SizingLabel));
    public static string DinRailTab_TooSmallWarning => Get(nameof(DinRailTab_TooSmallWarning));

    public static string BarcodeTab_DataLabel => Get(nameof(BarcodeTab_DataLabel));
    public static string BarcodeTab_DataPlaceholder => Get(nameof(BarcodeTab_DataPlaceholder));
    public static string BarcodeTab_SymbologyLabel => Get(nameof(BarcodeTab_SymbologyLabel));
    public static string BarcodeTab_ShowNumberCheckbox => Get(nameof(BarcodeTab_ShowNumberCheckbox));
    public static string BarcodeTab_NoDataEntered => Get(nameof(BarcodeTab_NoDataEntered));

    public static string HistoryTab_NoPrintsYet => Get(nameof(HistoryTab_NoPrintsYet));
    public static string HistoryTab_ReprintButton => Get(nameof(HistoryTab_ReprintButton));
    public static string HistoryTab_DeleteButton => Get(nameof(HistoryTab_DeleteButton));

    public static string AboutTab_Tagline => Get(nameof(AboutTab_Tagline));
    public static string AboutTab_VersionFormat => Get(nameof(AboutTab_VersionFormat));
    public static string AboutTab_RepositoryLabel => Get(nameof(AboutTab_RepositoryLabel));
    public static string AboutTab_LicenseLabel => Get(nameof(AboutTab_LicenseLabel));
    public static string AboutTab_UsedLibrariesHeader => Get(nameof(AboutTab_UsedLibrariesHeader));
    public static string AboutTab_AcknowledgementsHeader => Get(nameof(AboutTab_AcknowledgementsHeader));
}
