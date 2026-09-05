using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Letra200bSharp.Avalonia.ViewModels;
using Letra200bSharp.Avalonia.Views;
using SukiUI;

namespace Letra200bSharp.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        SyncSukiThemeWithSystem();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Avalonia's own Application.ActualThemeVariant already follows the OS light/dark
    /// setting on its own - it subscribes to PlatformSettings.ColorValuesChanged internally
    /// whenever RequestedThemeVariant is "Default" (as set in App.axaml). SukiUI's SukiTheme
    /// keeps its own separate base-theme state instead of reading that, though, so without
    /// this it never actually switches - most noticeable on Android, where there's no native
    /// window chrome around the app to hint that the OS setting was even read correctly.
    /// </summary>
    private void SyncSukiThemeWithSystem()
    {
        if (PlatformSettings is not { } settings)
        {
            return;
        }

        ApplySystemTheme(settings.GetColorValues());
        settings.ColorValuesChanged += (_, values) => ApplySystemTheme(values);
    }

    private static void ApplySystemTheme(PlatformColorValues values)
    {
        SukiTheme.GetInstance().ChangeBaseTheme((ThemeVariant)values.ThemeVariant);
    }
}