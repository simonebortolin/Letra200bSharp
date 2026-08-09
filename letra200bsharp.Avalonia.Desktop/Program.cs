using Avalonia;

namespace Letra200bSharp.Avalonia.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
#if WINDOWS
            // This app is built as WinExe (needed for the GUI to not pop up a console of its
            // own), which on Windows means stdout isn't connected to whatever terminal invoked
            // it - reattach to that terminal explicitly so CLI output is actually visible.
            // Harmless even if this turns out not to be a CLI invocation after all (see below).
            Windows.Win32.PInvoke.AttachConsole(Windows.Win32.PInvoke.ATTACH_PARENT_PROCESS);
#endif

            var exitCode = Cli.RunAsync(args).GetAwaiter().GetResult();
            if (exitCode.HasValue)
            {
#if WINDOWS
                // Detach cleanly before exiting, otherwise the parent shell's prompt can end
                // up racing our last bit of output instead of reliably appearing after it.
                Windows.Win32.PInvoke.FreeConsole();
#endif
                return exitCode.Value;
            }

            // args didn't start with one of our verbs - could be an Avalonia-specific flag
            // (e.g. a platform windowing option) rather than a CLI invocation, so fall through
            // and let the GUI have a shot at them instead of treating this as a usage error.
#if WINDOWS
            Windows.Win32.PInvoke.FreeConsole();
#endif
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
