using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace CS2Optimizer.Services;

/// <summary>A key combination like "Alt+W" or "Ctrl+Shift+F8".</summary>
public readonly record struct KeyCombo(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var mods = Modifiers;
        return string.Join("+", new[] { (ModifierKeys.Control, "Ctrl"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift"), (ModifierKeys.Windows, "Win") }
            .Where(m => mods.HasFlag(m.Item1)).Select(m => m.Item2).Append(KeyName(Key)));
    }

    public static KeyCombo? Parse(string text)
    {
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default:
                    if (Enum.TryParse<Key>(part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part, true, out var k)) key = k;
                    break;
            }
        }
        return key is { } kk && IsAllowed(mods, kk) ? new KeyCombo(mods, kk) : null;
    }

    /// <summary>A lone letter or digit would fire while typing in chat, so those need a modifier.</summary>
    public static bool IsAllowed(ModifierKeys mods, Key key) =>
        key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                    or Key.LWin or Key.RWin or Key.System or Key.Escape)
        && (mods != ModifierKeys.None || key is >= Key.F1 and <= Key.F24 or Key.Insert or Key.Home or Key.End
                or Key.PageUp or Key.PageDown or Key.Pause or Key.Scroll or >= Key.NumPad0 and <= Key.Divide);

    static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => key.ToString(),
    };
}

/// <summary>
/// System-wide watermark hotkey, owned by the main app (it lives on in the tray). Pressing it hides/shows a running
/// watermark at once through a named event, or starts the watermark when it is off.
/// </summary>
public sealed class WatermarkHotkey : IDisposable
{
    public static readonly string ToggleEventName = TestScope.Name(@"Local\CS2Optimizer.OverlayToggle");

    const int Id = 0xC520, WmHotkey = 0x0312;
    const uint ModNoRepeat = 0x4000;

    readonly HwndSource _window;
    // Created by the (non-elevated) main app so an elevated watermark can still open it.
    readonly EventWaitHandle _toggle = new(false, EventResetMode.AutoReset, ToggleEventName);
    bool _registered;

    public static WatermarkHotkey? Instance { get; private set; }

    /// <summary>Null when the hotkey is on and registered; otherwise why it isn't active.</summary>
    public string? Problem { get; private set; }

    public event Action? StateChanged;

    public WatermarkHotkey()
    {
        // Message-only window: works even when the main window was never shown (start in tray).
        _window = new HwndSource(new HwndSourceParameters("CS2Optimizer hotkey") { ParentWindow = new IntPtr(-3) });
        _window.AddHook(WndProc);
        AppSettings.Changed += Apply;
        Apply();
        Instance = this;
    }

    bool _paused;

    /// <summary>Off while the settings page records a new combination, so pressing the old one doesn't fire.</summary>
    public bool Paused
    {
        get => _paused;
        set { _paused = value; Apply(); }
    }

    void Apply()
    {
        if (_registered) { UnregisterHotKey(_window.Handle, Id); _registered = false; }
        var s = AppSettings.Current;
        if (_paused) Problem = null;
        else if (!s.HotkeyEnabled) Problem = "Бинд выключен.";
        else if (KeyCombo.Parse(s.Hotkey) is not { } combo) Problem = "Не удалось прочитать сочетание — задай его заново.";
        else
        {
            uint mods = ModNoRepeat
                        | (combo.Modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (combo.Modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0)
                        | (combo.Modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (combo.Modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0);
            _registered = RegisterHotKey(_window.Handle, Id, mods, (uint)KeyInterop.VirtualKeyFromKey(combo.Key));
            Problem = _registered ? null : $"{combo} уже занято другой программой — выбери другое сочетание.";
        }
        StateChanged?.Invoke();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            if (OverlayControl.IsRunning) _toggle.Set();
            else OverlayControl.Start(AppSettings.Current.OverlayAsAdmin);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        AppSettings.Changed -= Apply;
        if (_registered) UnregisterHotKey(_window.Handle, Id);
        _window.Dispose();
        _toggle.Dispose();
        if (Instance == this) Instance = null;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
