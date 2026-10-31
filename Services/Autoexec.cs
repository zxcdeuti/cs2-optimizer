namespace CS2Optimizer.Services;

/// <summary>
/// Writes named blocks into the game's autoexec.cfg, replacing the block in place on later saves
/// so the user's own lines and other blocks stay untouched.
/// </summary>
public static class Autoexec
{
    public static string? FilePath => Cs2Paths.CfgDir is { } dir ? Path.Combine(dir, "autoexec.cfg") : null;

    /// <summary>Returns the file written, or throws IOException/UnauthorizedAccessException.</summary>
    public static string WriteBlock(string name, IEnumerable<string> lines)
    {
        var file = FilePath ?? throw new IOException("Не нашёл папку CS2 в библиотеках Steam.");
        string start = $"// >>> CS2 Optimizer: {name}", end = $"// <<< CS2 Optimizer: {name}";
        var block = string.Join("\n", lines.Prepend(start).Append(end));

        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        int i = text.IndexOf(start, StringComparison.Ordinal);
        int j = text.IndexOf(end, StringComparison.Ordinal);
        text = i >= 0 && j > i
            ? text[..i] + block + text[(j + end.Length)..]
            : (text.Length > 0 ? text.TrimEnd() + "\n\n" : "") + block + "\n";
        File.WriteAllText(file, text);
        return file;
    }
}
