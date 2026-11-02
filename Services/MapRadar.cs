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

    // Part of the cache stamp: raising it re-extracts maps cached with older label positions.
    const long CacheFormat = 6;

    // Radar pixels of map a label wants around its spot.
    const int LabelMargin = 8;

    // Zones are rough boxes, so their middle can miss the spot players mean by that name. Radar position, 0–1.
    static readonly Dictionary<string, Dictionary<string, (double X, double Y)>> LabelSpots = new()
    {
        ["mirage"] = new()
        {
            ["SnipersNest"] = (0.397, 0.448), ["Underpass"] = (0.497, 0.249),
            ["CTSpawn"] = (0.302, 0.690), ["BombsiteA"] = (0.544, 0.764),
        },
        ["dust2"] = new() { ["BombsiteA"] = (0.795, 0.170), ["BombsiteB"] = (0.205, 0.130), ["CTSpawn"] = (0.580, 0.200), ["TSpawn"] = (0.385, 0.895), ["MidDoors"] = (0.452, 0.355), ["BDoors"] = (0.262, 0.228), ["TopofMid"] = (0.485, 0.630), ["LongDoors"] = (0.690, 0.600) },
        ["inferno"] = new() { ["BombsiteA"] = (0.815, 0.683), ["BombsiteB"] = (0.486, 0.194), ["CTSpawn"] = (0.895, 0.349), ["TSpawn"] = (0.100, 0.669) },
        ["nuke"] = new() { ["CTSpawn"] = (0.820, 0.465) },
        ["ancient"] = new() { ["BombsiteA"] = (0.302, 0.260), ["BombsiteB"] = (0.758, 0.405), ["TSpawn"] = (0.484, 0.863), ["Outside"] = (0.264, 0.657) },
        ["anubis"] = new() { ["CTSpawn"] = (0.404, 0.224), ["CTSideUpper"] = (0.449, 0.249), ["BombsiteB"] = (0.349, 0.507), ["BombsiteA"] = (0.752, 0.253) },
        ["cache"] = new() { ["BombsiteA"] = (0.317, 0.265), ["BombsiteB"] = (0.346, 0.809) },
        ["overpass"] = new() { ["BombsiteB"] = (0.695, 0.325) },
        ["vertigo"] = new() { ["BombsiteB"] = (0.218, 0.236) },
    };

    /// <summary>One env_cs_place volume in world units.</summary>
    record PlaceZone(string Name, Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) / 2;
    }

    /// <summary>Which radar pixels have map on them.</summary>
    record RadarMask(int Width, int Height, bool[] Opaque);

    static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2Optimizer", "maps");

    static readonly object LoadGate = new();

    static Dictionary<string, string>? _en, _ru;

    static string? GameCsgo => Cs2Paths.GameDir is { } d ? Path.Combine(d, "game", "csgo") : null;

    /// <summary>Whether this map was already pulled out of the game, so loading it should be quick.</summary>
    public static bool IsCached(string id) => File.Exists(Path.Combine(CacheDir, $"{id}.json"));

    /// <summary>Loads from cache, re-extracting when the map's files changed. Throws if CS2 isn't installed.</summary>
    public static RadarMap Load(string id)
    {
        var csgo = GameCsgo ?? throw new IOException("Не нашёл CS2 в библиотеках Steam.");
        var vpk = Path.Combine(csgo, "maps", $"de_{id}.vpk");
        if (!File.Exists(vpk)) throw new IOException($"В установленной игре нет карты de_{id}.");
        long stamp = File.GetLastWriteTimeUtc(vpk).Ticks ^ File.GetLastWriteTimeUtc(Path.Combine(csgo, "pak01_dir.vpk")).Ticks ^ CacheFormat;

        var json = Path.Combine(CacheDir, $"{id}.json");
        // The home screen and the maps page can ask for the same map at once, and both would write the same files.
        lock (LoadGate)
        {
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
        var masks = new Dictionary<string, RadarMask>();
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
            masks[layer] = Mask(bmp);

            var (l, t, r, b) = OpaqueBounds(bmp);
            (cl, ct, cr, cb) = (Math.Min(cl, l), Math.Min(ct, t), Math.Max(cr, r), Math.Max(cb, b));
        }
        if (layers.Count == 0) throw new IOException("Не нашёл радар карты в файлах игры.");

        _en ??= Localization(pak, "resource/csgo_english.txt");
        _ru ??= Localization(pak, "resource/csgo_russian.txt");

        // Place zones from every entity lump of the map.
        var raw = new List<PlaceZone>();
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
                        raw.Add(Zone(map, e));
            }
        }

        string LayerOf(float z) =>
            sections.FirstOrDefault(s => z >= s.Min && z < s.Max && layers.ContainsKey(s.Name)).Name ?? layers.Keys.First();

        var places = new List<PlaceMark>();
        foreach (var group in raw.GroupBy(r => (r.Name, Layer: LayerOf(r.Center.Z))))
        {
            foreach (var cluster in Cluster(group.ToList()))
            {
                var boxes = cluster.Select(z => (
                    L: (z.Min.X - posX) / scale / 1024, T: (posY - z.Max.Y) / scale / 1024,
                    R: (z.Max.X - posX) / scale / 1024, B: (posY - z.Min.Y) / scale / 1024)).ToList();
                var key = group.Key.Name;
                // One label for the whole place: its separate brushes would only repeat the name next to it.
                if (group.Key.Layer == "default" && LabelSpots.GetValueOrDefault(id)?.TryGetValue(key, out var spot) == true)
                {
                    places.Add(new PlaceMark(key, _en.GetValueOrDefault(key, Spaced(key)), _ru.GetValueOrDefault(key, Spaced(key)), spot.X, spot.Y, group.Key.Layer));
                    break;
                }
                var (x, y) = LabelSpot(boxes, masks[group.Key.Layer]);
                if (x is < 0 or > 1 || y is < 0 or > 1) continue;
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

    /// <summary>The volume an env_cs_place covers. Its origin alone is no use: it usually sits at a corner of the box.</summary>
    static PlaceZone Zone(Package map, ValveResourceFormat.ResourceTypes.EntityLump.Entity e)
    {
        var name = e["place_name"].ToString()!;
        var origin = e.GetVector3Property("origin", default);
        if (e.ContainsKey("model") && map.FindEntry(e["model"].ToString() + "_c") is { } entry)
        {
            map.ReadEntry(entry, out var bytes);
            using var res = new Resource();
            res.Read(new MemoryStream(bytes));
            var hulls = ((Model)res.DataBlock!).GetEmbeddedPhys()?.Parts.SelectMany(p => p.Shape.Hulls).ToList();
            if (hulls is { Count: > 0 })
                return new PlaceZone(name,
                    origin + hulls.Select(h => h.Shape.Min).Aggregate(Vector3.Min),
                    origin + hulls.Select(h => h.Shape.Max).Aggregate(Vector3.Max));
        }
        return new PlaceZone(name, origin, origin);
    }

    static RadarMask Mask(SKBitmap bmp)
    {
        var opaque = new bool[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                opaque[y * bmp.Width + x] = bmp.GetPixel(x, y).Alpha >= 24;
        return new RadarMask(bmp.Width, bmp.Height, opaque);
    }

    /// <summary>
    /// The middle of the part of a zone that is actually map, since its box also covers walls and voids.
    /// On an L-shaped area that middle can land off the map, so it then moves to the nearest map pixel.
    /// </summary>
    static (double X, double Y) LabelSpot(List<(double L, double T, double R, double B)> boxes, RadarMask mask)
    {
        var pixels = new List<(int X, int Y)>();
        foreach (var (l, t, r, b) in boxes)
        {
            int x0 = Math.Max(0, (int)(l * mask.Width)), x1 = Math.Min(mask.Width - 1, (int)(r * mask.Width));
            int y0 = Math.Max(0, (int)(t * mask.Height)), y1 = Math.Min(mask.Height - 1, (int)(b * mask.Height));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (mask.Opaque[y * mask.Width + x]) pixels.Add((x, y));
        }
        if (pixels.Count == 0)
            return ((boxes[0].L + boxes[0].R) / 2, (boxes[0].T + boxes[0].B) / 2);

        double cx = pixels.Average(p => p.X), cy = pixels.Average(p => p.Y);
        // Prefer pixels with map all around them, so a label doesn't end up on a wall's edge.
        bool Open(int x, int y) => x >= 0 && y >= 0 && x < mask.Width && y < mask.Height && mask.Opaque[y * mask.Width + x];
        var inner = pixels.Where(p => Open(p.X - LabelMargin, p.Y) && Open(p.X + LabelMargin, p.Y) && Open(p.X, p.Y - LabelMargin) && Open(p.X, p.Y + LabelMargin)).ToList();
        var (bx, by) = (inner.Count > 0 ? inner : pixels).MinBy(p => (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        return ((double)bx / mask.Width, (double)by / mask.Height);
    }

    static List<List<PlaceZone>> Cluster(List<PlaceZone> zones)
    {
        var clusters = new List<List<PlaceZone>>();
        foreach (var z in zones)
        {
            var near = clusters.FirstOrDefault(c => c.Any(q => Vector2.Distance(new(z.Center.X, z.Center.Y), new(q.Center.X, q.Center.Y)) < ClusterDistance));
            if (near != null) near.Add(z); else clusters.Add([z]);
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
