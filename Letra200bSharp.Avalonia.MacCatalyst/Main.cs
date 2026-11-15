using UIKit;

namespace Letra200bSharp.Avalonia.MacCatalyst;

public static class Program
{
    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        // "AppDelegate" is registered with the Objective-C runtime by [Register] above.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
