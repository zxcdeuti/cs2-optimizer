using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace CS2Optimizer.Services;

/// <summary>Isolates debug test runs from a normally running copy. In release builds names are unchanged.</summary>
public static class TestScope
{
    public static string Name(string name)
    {
#if DEBUG
        return name + (Arg("--test-scope") ?? Environment.GetEnvironmentVariable("CS2OPT_TEST_SCOPE"));
#else
        return name;
#endif
    }

#if DEBUG
    /// <summary>Elevated test runs don't inherit environment variables, so they pass their options as arguments.</summary>
    public static string? Arg(string key)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
#endif
}

/// <summary>
/// Self-update from this project's GitHub releases. Only the published single-file build updates itself. A download is
/// installed only if its SHA-256 matches the digest GitHub reports, and never while the game or the watermark runs.
/// </summary>
public sealed class Updater
{
    const string LatestRelease = "https://api.github.com/repos/zxcdeuti/cs2-optimizer/releases/latest";
    const string DownloadPrefix = "https://github.com/zxcdeuti/cs2-optimizer/releases/download/";
    const string ExeName = "CS2Optimizer.exe";

    static readonly string StageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2Optimizer", "update");

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    readonly DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromHours(1) };
    readonly DispatcherTimer _installTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    bool _busy;
    string? _stagedExe;

    public static Updater Instance { get; } = new();

    public string? ReadyVersion { get; private set; }
    public string Status { get; private set; } = "Ещё не проверялось.";

    /// <summary>Raised on the UI thread when Status or ReadyVersion change.</summary>
    public event Action? Changed;

    /// <summary>Called when it's a good moment to install: restart into the new version, keeping the window hidden.</summary>
    public event Action? QuietInstallPossible;

    Updater()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2Optimizer-Updater");
        _checkTimer.Tick += async (_, _) => await Check();
        _installTimer.Tick += (_, _) => { if (ReadyVersion != null) QuietInstallPossible?.Invoke(); };
    }

    /// <summary>A dev build runs from a folder of DLLs that a single downloaded exe couldn't replace.</summary>
    public static bool CanUpdateItself =>
        Environment.ProcessPath is { } exe
        && !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "CS2Optimizer.dll"));

    public static string CurrentVersion => Normalize(typeof(App).Assembly.GetName().Version!).ToString(3);

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public void Start()
    {
        RemoveOldExes();
        // Anything staged by an earlier run is either installed by now or will be fetched and verified again.
        try { if (Directory.Exists(StageDir)) Directory.Delete(StageDir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!CanUpdateItself)
        {
            Say("Автообновление работает в версии из релиза, а это сборка для разработки.");
            return;
        }
        _checkTimer.Start();
        _installTimer.Start();
#if DEBUG
        if (Environment.GetEnvironmentVariable("CS2OPT_UPDATE_FEED") != null)
            _installTimer.Interval = TimeSpan.FromSeconds(2);
#endif
        _ = CheckAfterStartup();
    }

    async Task CheckAfterStartup()
    {
#if DEBUG
        await Task.Delay(TimeSpan.FromSeconds(1));
#else
        await Task.Delay(TimeSpan.FromSeconds(20)); // don't compete with the app's own startup
#endif
        await Check();
    }

    public async Task Check()
    {
        if (_busy || !CanUpdateItself || !AppSettings.Current.AutoUpdate) return;
        _busy = true;
        try
        {
            Say("Проверяю обновления…");
            var feed = LatestRelease;
            var prefix = DownloadPrefix;
#if DEBUG
            if (Environment.GetEnvironmentVariable("CS2OPT_UPDATE_FEED") is { } testFeed)
            {
                feed = testFeed + "/latest.json";
                prefix = testFeed + "/";
            }
#endif
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(feed));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out var parsed)) { Say($"Не понял номер версии «{tag}»."); return; }
            var latest = Normalize(parsed);
            if (latest <= Version.Parse(CurrentVersion))
            {
                Say($"Установлена последняя версия ({CurrentVersion}). Проверено в {DateTime.Now:HH:mm}.");
                return;
            }
            if (ReadyVersion == latest.ToString(3)) return;

            JsonElement? asset = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString() is { } n && n.StartsWith("CS2Optimizer-") && n.EndsWith("-win-x64.zip")) asset = a;
            if (asset is not { } found) { Say($"В релизе {latest.ToString(3)} нет файла для обновления."); return; }

            var url = found.GetProperty("browser_download_url").GetString() ?? "";
            var digest = found.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            // Both checks fail closed: a release without a digest, or pointing elsewhere, is never installed.
            if (!url.StartsWith(prefix, StringComparison.Ordinal) || !digest.StartsWith("sha256:"))
            {
                Say($"Обновление {latest.ToString(3)} не прошло проверку источника и не будет установлено.");
                return;
            }

            Say($"Скачиваю {latest.ToString(3)}…");
            _stagedExe = await Download(url, digest["sha256:".Length..], latest.ToString(3));
            if (_stagedExe == null) { Say($"Файл {latest.ToString(3)} не совпал с контрольной суммой и удалён."); return; }

            ReadyVersion = latest.ToString(3);
            Say($"Версия {ReadyVersion} загружена и проверена.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException
                                      or InvalidDataException or KeyNotFoundException or UnauthorizedAccessException)
        {
            Say("Не удалось проверить обновления. Повторю через час.");
        }
        finally
        {
            _busy = false;
        }
    }

    async Task<string?> Download(string url, string expectedSha256, string version)
    {
        Directory.CreateDirectory(StageDir);
        var zip = Path.Combine(StageDir, $"{version}.zip");
        if (File.Exists(zip)) File.Delete(zip);

        using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await response.Content.CopyToAsync(file);
        }

        string actual;
        await using (var read = File.OpenRead(zip))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(read));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zip);
            return null;
        }

        var exe = Path.Combine(StageDir, ExeName);
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.GetEntry(ExeName) ?? throw new InvalidDataException("В архиве нет exe.");
        entry.ExtractToFile(exe, overwrite: true);
        return exe;
    }

    /// <summary>
    /// Swaps the exe: a running file can't be overwritten but can be renamed. The copy of the old one is removed on the
    /// next start. Returns false and restores the old exe if the folder isn't writable.
    /// </summary>
    public bool SwapExe()
    {
        if (_stagedExe == null || !File.Exists(_stagedExe) || Environment.ProcessPath is not { } target) return false;
        var old = $"{target}.old-{DateTime.Now.Ticks}";
        try
        {
            File.Move(target, old);
            File.Copy(_stagedExe, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(target) && File.Exists(old)) File.Move(old, target);
            Say($"Не получилось заменить файл: папка приложения недоступна для записи ({Path.GetDirectoryName(target)}).");
            return false;
        }
    }

    static void RemoveOldExes()
    {
        if (Environment.ProcessPath is not { } exe) return;
        foreach (var old in Directory.GetFiles(Path.GetDirectoryName(exe)!, Path.GetFileName(exe) + ".old-*"))
        {
            try { File.Delete(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // e.g. the watermark still runs from it
        }
    }

    public static bool GameOrOverlayRunning => Cs2Paths.IsGameRunning || OverlayControl.IsRunning;

    void Say(string status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
