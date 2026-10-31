using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace CS2Optimizer.Services;

/// <param name="Key">Valve place name, e.g. "LongDoors".</param>
/// <param name="X">Position on the radar, 0–1 from the left.</param>
/// <param name="Y">Position on the radar, 0–1 from the top.</param>
public record PlaceMark(string Key, string En, string Ru, double X, double Y, string Layer);

/// <param name="Layers">Layer name ("default", "lower") → cached radar PNG.</param>
/// <param name="Crop">Part of the radar that has map on it, 0–1: left, top, right, bottom (union of all layers).</param>
public record RadarMap(string Id, long Stamp, Dictionary<string, string> Layers, List<PlaceMark> Places, double[]? Crop);

/// <summary>
/// Pulls radar images and the official callout zones (env_cs_place entities — the names the game shows next to
/// a teammate) straight out of the user's CS2 install, projects them onto the radar and caches the result.
/// </summary>
public static class MapRadar
{
    public static readonly (string Id, string Name)[] Pool =
    [
        // Active Duty since July 2026 first, then maps that left the pool but are still in the game.
        ("dust2", "Dust II"), ("mirage", "Mirage"), ("inferno", "Inferno"), ("nuke", "Nuke"), ("ancient", "Ancient"),
        ("anubis", "Anubis"), ("cache", "Cache"), ("overpass", "Overpass"), ("train", "Train"), ("vertigo", "Vertigo"),
    ];

    // Separate brushes of one place further apart than this (world units) get separate labels.
    const float ClusterDistance = 700;

    static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2Optimizer", "maps");

    static Dictionary<string, string>? _en, _ru;

    static string? GameCsgo => Cs2Paths.GameDir is { } d ? Path.Combine(d, "game", "csgo") : null;

    /// <summary>Loads from cache, re-extracting when the map's files changed. Throws if CS2 isn't installed.</summary>
    public static RadarMap Load(string id)
    {
        var csgo = GameCsgo ?? throw new IOException("Не нашёл CS2 в библиотеках Steam.");
        var vpk = Path.Combine(csgo, "maps", $"de_{id}.vpk");
        if (!File.Exists(vpk)) throw new IOException($"В установленной игре нет карты de_{id}.");
        long stamp = File.GetLastWriteTimeUtc(vpk).Ticks ^ File.GetLastWriteTimeUtc(Path.Combine(csgo, "pak01_dir.vpk")).Ticks;

        var json = Path.Combine(CacheDir, $"{id}.json");
        try
        {
            if (JsonSerializer.Deserialize<RadarMap>(File.ReadAllText(json)) is { } cached
                && cached.Stamp == stamp && cached.Crop != null && cached.Layers.Values.All(File.Exists))
                return cached;
        }
        catch (Exception e) when (e is IOException or JsonException) { }

        var map = Extract(id, csgo, vpk, stamp);
        Directory.CreateDirectory(CacheDir);
        File.WriteAllText(json, JsonSerializer.Serialize(map));
        return map;
    }

    static RadarMap Extract(string id, string csgo, string vpk, long stamp)
    {
        using var pak = new Package();
        pak.Read(Path.Combine(csgo, "pak01_dir.vpk"));

        // Overview: world → radar transform and altitude bands for multi-level maps.
        var overview = ReadText(pak, $"resource/overviews/de_{id}.txt") ?? throw new IOException("Нет файла обзора карты.");
        double posX = Num(overview, "pos_x"), posY = Num(overview, "pos_y"), scale = Num(overview, "scale");
        var sections = new List<(string Name, double Min, double Max)>();
        var vs = Regex.Match(overview, "\"verticalsections\".*", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (vs.Success)
            foreach (Match m in Regex.Matches(vs.Value, "\"(\\w+)\"[^{]*\\{\\s*\"AltitudeMax\"\\s*\"([-\\d.]+)\"\\s*\"AltitudeMin\"\\s*\"([-\\d.]+)\"", RegexOptions.IgnoreCase))
                sections.Add((m.Groups[1].Value, Inv(m.Groups[3].Value), Inv(m.Groups[2].Value)));

        Directory.CreateDirectory(CacheDir);
        var layers = new Dictionary<string, string>();
        double cl = 1, ct = 1, cr = 0, cb = 0;
        foreach (var layer in sections.Select(s => s.Name).DefaultIfEmpty("default"))
        {
            var tex = layer == "default" ? $"de_{id}_radar_psd" : $"de_{id}_{layer}_radar_psd";
            var entry = pak.FindEntry($"panorama/images/overheadmaps/{tex}.vtex_c");
            if (entry == null) continue;
            pak.ReadEntry(entry, out var bytes);
            using var res = new Resource();
            res.Read(new MemoryStream(bytes));
            using var bmp = ((Texture)res.DataBlock!).GenerateBitmap();
            var png = Path.Combine(CacheDir, $"{id}_{layer}.png");
            using (var file = File.Create(png)) bmp.Encode(file, SKEncodedImageFormat.Png, 100);
            layers[layer] = png;

            var (l, t, r, b) = OpaqueBounds(bmp);
            (cl, ct, cr, cb) = (Math.Min(cl, l), Math.Min(ct, t), Math.Max(cr, r), Math.Max(cb, b));
        }
        if (layers.Count == 0) throw new IOException("Не нашёл радар карты в файлах игры.");

        _en ??= Localization(pak, "resource/csgo_english.txt");
        _ru ??= Localization(pak, "resource/csgo_russian.txt");

        // Place zones from every entity lump of the map.
        var raw = new List<(string Name, Vector3 Origin)>();
        using (var map = new Package())
        {
            map.Read(vpk);
            foreach (var entry in map.Entries?.GetValueOrDefault("vents_c") ?? [])
            {
                map.ReadEntry(entry, out var bytes);
                using var res = new Resource();
                res.Read(new MemoryStream(bytes));
                foreach (var e in ((EntityLump)res.DataBlock!).GetEntities())
                    if (e.ContainsKey("classname") && e["classname"].ToString() == "env_cs_place" && e.ContainsKey("place_name"))
                        raw.Add((e["place_name"].ToString()!, e.GetVector3Property("origin", default)));
            }
        }

        string LayerOf(float z) =>
            sections.FirstOrDefault(s => z >= s.Min && z < s.Max && layers.ContainsKey(s.Name)).Name ?? layers.Keys.First();

        var places = new List<PlaceMark>();
        foreach (var group in raw.GroupBy(r => (r.Name, Layer: LayerOf(r.Origin.Z))))
        {
            foreach (var cluster in Cluster(group.Select(g => g.Origin).ToList()))
            {
                var c = cluster.Aggregate(Vector3.Zero, (a, b) => a + b) / cluster.Count;
                double x = (c.X - posX) / scale / 1024, y = (posY - c.Y) / scale / 1024;
                if (x is < 0 or > 1 || y is < 0 or > 1) continue;
                var key = group.Key.Name;
                places.Add(new PlaceMark(key, _en.GetValueOrDefault(key, Spaced(key)), _ru.GetValueOrDefault(key, Spaced(key)), x, y, group.Key.Layer));
            }
        }
        // Square crop around the map with a little margin, so sparse radars (Nuke) get zoomed in.
        double size = Math.Min(1, Math.Max(cr - cl, cb - ct) + 0.06);
        double cx = (cl + cr) / 2, cy = (ct + cb) / 2;
        double left = Math.Clamp(cx - size / 2, 0, 1 - size), top = Math.Clamp(cy - size / 2, 0, 1 - size);
        return new RadarMap(id, stamp, layers, places, [left, top, left + size, top + size]);
    }

    /// <summary>Normalised bounds of pixels that aren't (nearly) transparent.</summary>
    static (double L, double T, double R, double B) OpaqueBounds(SKBitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                if (bmp.GetPixel(x, y).Alpha < 24) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        return maxX < minX ? (0, 0, 1, 1) : ((double)minX / bmp.Width, (double)minY / bmp.Height, (double)maxX / bmp.Width, (double)maxY / bmp.Height);
    }

    static List<List<Vector3>> Cluster(List<Vector3> points)
    {
        var clusters = new List<List<Vector3>>();
        foreach (var p in points)
        {
            var near = clusters.FirstOrDefault(c => c.Any(q => Vector2.Distance(new(p.X, p.Y), new(q.X, q.Y)) < ClusterDistance));
            if (near != null) near.Add(p); else clusters.Add([p]);
        }
        return clusters;
    }

    static string? ReadText(Package pak, string path)
    {
        var entry = pak.FindEntry(path);
        if (entry == null) return null;
        pak.ReadEntry(entry, out var bytes);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    static Dictionary<string, string> Localization(Package pak, string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = ReadText(pak, path);
        if (text == null) return dict;
        foreach (Match m in Regex.Matches(text, "^\\s*\"([A-Za-z0-9_]+)\"\\s+\"([^\"\\r\\n]*)\"", RegexOptions.Multiline))
            dict.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
        return dict;
    }

    static double Num(string kv, string key) =>
        Inv(Regex.Match(kv, $"\"{key}\"\\s+\"([-\\d.]+)\"").Groups[1].Value);

    static double Inv(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>"LongDoors" → "Long Doors" when the game has no translation.</summary>
    static string Spaced(string key) => Regex.Replace(key, "(?<=[a-z])(?=[A-Z])", " ");
}
