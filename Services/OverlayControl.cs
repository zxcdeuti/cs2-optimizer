using System.ComponentModel;
using System.Diagnostics;
using CS2Optimizer.Views;

namespace CS2Optimizer.Services;

/// <summary>Starts and stops the watermark process; shared by its page, the tray menu and autostart.</summary>
public static class OverlayControl
{
    public static bool IsRunning => OverlayWindow.IsRunning();

    public static void Start(bool asAdmin)
    {
        if (IsRunning) return;
        SetEnabled(true);
        var exe = Environment.ProcessPath!;
        if (asAdmin)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe, "--overlay") { UseShellExecute = true, Verb = "runas" });
                return;
            }
            catch (Win32Exception) { } // UAC declined: run without admin — everything but FPS still works
        }
        Process.Start(new ProcessStartInfo(exe, "--overlay") { UseShellExecute = true });
    }

    /// <summary>The overlay polls its settings file and closes itself within a second — even when elevated.</summary>
    public static void Stop() => SetEnabled(false);

    static void SetEnabled(bool on) => (OverlaySettings.Load() with { Enabled = on }).Save();
}
