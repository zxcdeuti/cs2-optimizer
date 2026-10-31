using System.Text.RegularExpressions;

namespace CS2Optimizer.Services;

/// <summary>Edits cs2_video.txt in place, keeping every line it doesn't touch.</summary>
public class VideoConfig
{
    static readonly Regex Pair = new("^(\\s*)\"([^\"]+)\"(\\s+)\"([^\"]*)\"");

    readonly List<string> _lines;
    public string FilePath { get; }

    public VideoConfig(string path)
    {
        FilePath = path;
        _lines = File.ReadAllLines(path).ToList();
    }

    public string? Get(string key)
    {
        foreach (var line in _lines)
            if (Pair.Match(line) is { Success: true } m && m.Groups[2].Value == key)
                return m.Groups[4].Value;
        return null;
    }

    public void Set(string key, string value)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (Pair.Match(_lines[i]) is { Success: true } m && m.Groups[2].Value == key)
            {
                _lines[i] = $"{m.Groups[1].Value}\"{key}\"{m.Groups[3].Value}\"{value}\"";
                return;
            }
        }
        int close = _lines.FindLastIndex(l => l.Trim() == "}");
        _lines.Insert(close < 0 ? _lines.Count : close, $"\t\"{key}\"\t\t\"{value}\"");
    }

    /// <summary>Backs up the file on disk, then writes the edited lines. Returns the backup path.</summary>
    public string Save()
    {
        var backup = $"{FilePath}.bak_{DateTime.Now:yyyyMMdd_HHmmss}";
        File.Copy(FilePath, backup, overwrite: true);
        File.WriteAllLines(FilePath, _lines);
        return backup;
    }

    public static string? LatestBackup(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bak_*")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
}
