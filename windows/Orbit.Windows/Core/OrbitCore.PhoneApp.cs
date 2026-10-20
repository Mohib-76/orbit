using System.IO;

namespace Orbit.Core;

/// <summary>Is Orbit on the plugged-in phone, and is it as new as the copy this PC can install?</summary>
public enum PhoneAppState { Unknown, Missing, Older, Current }

/// <summary>
/// Install over USB: the Windows app ships the matching Android APK (phone\Orbit.apk) and installs or updates
/// it through adb. Installs from adb aren't blocked by Play Protect's sideloading checks, and don't count as
/// a "restricted" sideload, so notification access can be turned on normally afterwards.
/// </summary>
public sealed partial class OrbitCore
{
    /// <summary>The bundled phone app, or null in builds that don't include it (e.g. a plain dotnet build).</summary>
    public static string? BundledApk => _bundledApk.Value;
    static readonly Lazy<string?> _bundledApk = new(() =>
        Path.Combine(AppContext.BaseDirectory, "phone", "Orbit.apk") is var p && File.Exists(p) ? p : null);

    /// <summary>Refreshes can overlap (device events, the install itself); only the newest one is applied.</summary>
    int _phoneAppSeq;
    PhoneAppState _phoneApp;
    string _phoneAppVersion = "";
    bool _phoneAppInstalling;

    public PhoneAppState PhoneApp { get => _phoneApp; private set => Set(ref _phoneApp, value); }
    /// <summary>versionName of Orbit on the plugged-in phone, "" when unknown or not installed.</summary>
    public string PhoneAppVersion { get => _phoneAppVersion; private set => Set(ref _phoneAppVersion, value); }
    public bool PhoneAppInstalling { get => _phoneAppInstalling; private set => Set(ref _phoneAppInstalling, value); }

    /// <summary>True when a phone is on USB with debugging allowed and this build carries the phone app.</summary>
    public bool CanInstallPhoneApp => BundledApk != null && Adb.FirstReadySerial() != null && !PhoneAppInstalling;

    async Task RefreshPhoneAppAsync(string? serial)
    {
        var seq = Interlocked.Increment(ref _phoneAppSeq);
        var state = PhoneAppState.Unknown;
        var version = "";
        if (serial != null)
        {
            var (code, o) = await Adb.ShellAsync(serial, $"dumpsys package {Adb.PhonePackage}", 10000);
            var line = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("versionName="));
            if (code == 0 && line != null)
            {
                version = line["versionName=".Length..].Trim();
                state = IsOlder(version, Proto.AppVersion) ? PhoneAppState.Older : PhoneAppState.Current;
            }
            else if (code == 0) state = PhoneAppState.Missing;
        }
        Ui.Run(() =>
        {
            if (seq != Volatile.Read(ref _phoneAppSeq)) return;   // a newer refresh started meanwhile
            PhoneAppVersion = version;
            PhoneApp = state;
            Raise(nameof(CanInstallPhoneApp));
        });
    }

    /// <summary>"1.0.0" is older than "1.1.0". Unparseable versions never count as older.</summary>
    public static bool IsOlder(string version, string than) =>
        Version.TryParse(version.Split('-', '+')[0], out var a) && Version.TryParse(than.Split('-', '+')[0], out var b) && a < b;

    /// <summary>
    /// Installs (or updates) the bundled phone app on the plugged-in phone. When this PC isn't paired yet, it
    /// then opens Orbit on the phone and pairs over USB. Returns a short message for the UI.
    /// </summary>
    public async Task<string> InstallPhoneAppAsync()
    {
        var apk = BundledApk;
        if (apk == null) return "This build doesn't include the phone app. Download it from the Orbit website.";
        var serial = Adb.FirstReadySerial();
        if (serial == null) return "Connect your phone with USB debugging on.";
        if (PhoneAppInstalling) return "Already installing…";

        // A fresh install (not an update) gives the phone new keys, so an existing pairing can't work any more.
        var fresh = PhoneApp == PhoneAppState.Missing;
        Ui.Run(() => { PhoneAppInstalling = true; Raise(nameof(CanInstallPhoneApp)); });
        try
        {
            Log.Write("phone app: installing over USB" + (fresh ? "" : " (update)"));
            // -r keeps data when updating; --user current skips other profiles (work profile, Secure Folder),
            // which adb's shell user often isn't allowed to touch. Some phones ask to confirm on screen: wait.
            var (code, o) = await Adb.RunAsync($"-s {serial} install -r --user current \"{apk}\"", 180000);
            var ok = code == 0 && o.Contains("Success");
            // Only adb's failure code: its full output names the APK's path, which includes the Windows user name.
            Log.Write("phone app: " + (ok ? "installed" : "install failed: " + FailureCode(o)));
            await RefreshPhoneAppAsync(serial);
            if (!ok) return InstallError(o);

            // Same as plugging in a phone that has Orbit: USB link (adb forward), USB mode, pairing allowed.
            _usbSerial = null;
            await OnUsbDeviceOnlineAsync(serial);
            if (fresh && Settings.Phone != null) { Log.Write("phone app: fresh install, pairing again"); Unpair(); }
            if (Settings.Phone == null) return await PairOverUsbAsync();
            Ui.Run(() => Toasts.Simple("Orbit updated on your phone", $"Your phone now has Orbit {Proto.AppVersion}."));
            return "Orbit on your phone is up to date.";
        }
        finally
        {
            Ui.Run(() => { PhoneAppInstalling = false; Raise(nameof(CanInstallPhoneApp)); });
        }
    }

    /// <summary>"INSTALL_FAILED_…" (or "timeout" / "unknown") from adb's output, without paths.</summary>
    public static string FailureCode(string output) =>
        System.Text.RegularExpressions.Regex.Match(output, @"INSTALL_[A-Z_]+|INSTALL_FAILED|timeout") is { Success: true } m
            ? m.Value : "unknown";

    /// <summary>adb's failure codes, in words a user can act on.</summary>
    public static string InstallError(string output) => output switch
    {
        _ when output.Contains("timeout") => "The phone didn't answer. If it's asking to allow the install, tap Allow and try again.",
        _ when output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") =>
            "A differently signed Orbit is on your phone. Uninstall it there, then try again.",
        _ when output.Contains("INSTALL_FAILED_VERSION_DOWNGRADE") => "Your phone already has a newer Orbit.",
        _ when output.Contains("INSTALL_FAILED_USER_RESTRICTED") || output.Contains("INSTALL_FAILED_ABORTED") =>
            "The phone refused the install. Allow it on the phone; on Xiaomi, Redmi and POCO also turn on \"Install via USB\" in Developer options.",
        _ when output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE") => "Your phone is out of storage.",
        _ => $"Couldn't install Orbit on the phone ({FailureCode(output)}). Unplug, plug in again and retry.",
    };
}
