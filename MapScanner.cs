using System.Text;
using System.Text.RegularExpressions;

namespace Ra2ModeLauncher;

internal static partial class MapScanner
{
    public static List<MapInfo> Scan(string gamePath)
    {
        if (!Directory.Exists(gamePath))
            return [];

        return Directory.EnumerateFiles(gamePath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".yrm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            .Select(Read)
            .Where(map => map.StartingPoints > 1)
            .OrderBy(map => map.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static MapInfo Read(string path)
    {
        string text = ReadText(path);
        int count = ParseInt(NumberStartingPointsRegex().Match(text).Groups[1].Value);
        if (count == 0)
            count = WaypointRegex().Matches(text).Select(m => ParseInt(m.Groups[1].Value)).DefaultIfEmpty(0).Max();

        string mapName = BasicNameRegex().Match(text).Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(mapName))
            mapName = Path.GetFileNameWithoutExtension(path);

        List<PointF> starts = ParseStartPositions(text);
        if (count == 0) count = starts.Count;
        return new MapInfo(path, mapName, Math.Clamp(count, 0, 8), starts);
    }

    private static List<PointF> ParseStartPositions(string text)
    {
        Dictionary<string, string> map = ReadSection(text, "Map");
        Dictionary<string, string> waypoints = ReadSection(text, "Waypoints");
        if (!TryFourInts(map.GetValueOrDefault("Size"), out int _, out int _, out int actualWidth, out int _) ||
            !TryFourInts(map.GetValueOrDefault("LocalSize"), out int localX, out int localY, out int localWidth, out int localHeight) ||
            actualWidth <= 0 || localWidth <= 0 || localHeight <= 0) return [];

        var result = new List<PointF>();
        for (int i = 0; i < 8; i++)
        {
            if (!waypoints.TryGetValue(i.ToString(), out string? raw)) break;
            string coordinate = raw.Split(',')[0].Trim();
            if (coordinate.Length < 4 || !int.TryParse(coordinate, out _)) break;
            int split = coordinate.Length - 3;
            if (!int.TryParse(coordinate[..split], out int isoY) || !int.TryParse(coordinate[split..], out int isoX)) break;
            int rx = isoX - isoY + actualWidth - 1;
            int ry = isoX + isoY - actualWidth - 1;
            float x = (rx / 2f - localX) / localWidth;
            float y = (ry / 2f - localY) / localHeight;
            result.Add(new PointF(Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f)));
        }
        return result;
    }

    private static Dictionary<string, string> ReadSection(string text, string section)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool active = false;
        foreach (string sourceLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = sourceLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { active = line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (!active || line.StartsWith(';')) continue;
            int equals = line.IndexOf('=');
            if (equals > 0) values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return values;
    }

    private static bool TryFourInts(string? value, out int a, out int b, out int c, out int d)
    {
        a = b = c = d = 0;
        string[] parts = value?.Split(',') ?? [];
        return parts.Length >= 4 && int.TryParse(parts[0], out a) && int.TryParse(parts[1], out b) && int.TryParse(parts[2], out c) && int.TryParse(parts[3], out d);
    }

    private static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes);
        return Encoding.Default.GetString(bytes);
    }

    private static int ParseInt(string value) => int.TryParse(value, out int parsed) ? parsed : 0;

    [GeneratedRegex(@"(?im)^NumberStartingPoints\s*=\s*(\d+)\s*$")]
    private static partial Regex NumberStartingPointsRegex();

    [GeneratedRegex(@"(?im)^Waypoint(\d+)\s*=\s*[^\r\n]+$")]
    private static partial Regex WaypointRegex();

    [GeneratedRegex(@"(?ims)^\[Basic\].*?^Name\s*=\s*([^\r\n]+)")]
    private static partial Regex BasicNameRegex();
}
