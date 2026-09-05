using System;
using System.Linq;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.SplashScreen;
using Avalonia;
using Avalonia.Android;

namespace Letra200bSharp.Avalonia.Android;

[Activity(
    Label = "@string/app_name",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Must run before base.OnCreate() so the library takes over the launch window
        // before the activity creates it - on Android 12+, calling it any later means
        // postSplashScreenTheme is silently ignored and windowBackground stays @null.
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
        RequestBluetoothPermissions();
    }

    /// <summary>
    /// Bluetooth LE scanning is a "dangerous" permission on Android and must be granted at
    /// runtime, not just declared in the manifest - without this, LetraPrinter.ScanForDevicesAsync
    /// fails with "Need android.permission.BLUETOOTH_SCAN permission ...".
    /// </summary>
    private void RequestBluetoothPermissions()
    {
        // "Android" here would otherwise resolve to the nested namespace
        // "Letra200bSharp.Avalonia.Android" (our own project) rather than the root
        // "Android" bindings namespace, since this class's own namespace ends in
        // ".Android" too - qualify with global:: to disambiguate.
        string[] permissions;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // Android 12+ (API 31+): dedicated Bluetooth runtime permissions.
            permissions = new[] { global::Android.Manifest.Permission.BluetoothScan, global::Android.Manifest.Permission.BluetoothConnect };
        }
        else
        {
            // Older Android: BLE scanning requires (coarse/fine) location permission.
            permissions = new[] { global::Android.Manifest.Permission.AccessFineLocation };
        }

        var missing = permissions.Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (missing.Length > 0)
        {
            RequestPermissions(missing, 0);
        }
    }
}
