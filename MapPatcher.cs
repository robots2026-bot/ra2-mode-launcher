using System.Text;
using System.Text.RegularExpressions;

namespace Ra2ModeLauncher;

internal static class MapPatcher
{
    public static void AddChronoLegionnaireWallPassThrough(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = DetectEncoding(bytes);
        string text = encoding.GetString(bytes);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // Keep the original InvisibleLow terrain/elevation behaviour, but give
        // Chrono Legionnaire weapons their own projectile so other weapons that
        // share InvisibleLow do not start firing through walls as a side effect.
        text = SetSectionValue(text, "RA2ModeChronoProjectile", "Inviso", "yes", newline);
        text = SetSectionValue(text, "RA2ModeChronoProjectile", "Image", "none", newline);
        text = SetSectionValue(text, "RA2ModeChronoProjectile", "SubjectToCliffs", "yes", newline);
        text = SetSectionValue(text, "RA2ModeChronoProjectile", "SubjectToElevation", "yes", newline);
        text = SetSectionValue(text, "RA2ModeChronoProjectile", "SubjectToWalls", "no", newline);
        text = SetSectionValue(text, "NeutronRifle", "Projectile", "RA2ModeChronoProjectile", newline);
        text = SetSectionValue(text, "NeutronRifle", "Range", "8", newline);
        text = SetSectionValue(text, "NeutronRifleE", "Projectile", "RA2ModeChronoProjectile", newline);
        text = SetSectionValue(text, "NeutronRifleE", "Range", "9", newline);
        text = SetSectionValue(text, "CRNeutronRifle", "Projectile", "RA2ModeChronoProjectile", newline);
        text = SetSectionValue(text, "CRNeutronRifle", "Range", "10", newline);

        File.WriteAllText(path, text, encoding);
    }

    public static void AddRevealAllTrigger(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = DetectEncoding(bytes);
        string text = encoding.GetString(bytes);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        text = SetSectionValue(text, "MultiplayerDialogSettings", "Shroud", "no", newline);
        File.WriteAllText(path, text, encoding);
    }

    private static string SetSectionValue(string text, string section, string key, string value, string newline)
    {
        var sectionRegex = new Regex($@"(?im)^\[{Regex.Escape(section)}\]\s*$", RegexOptions.CultureInvariant);
        Match sectionMatch = sectionRegex.Match(text);
        if (!sectionMatch.Success)
            return text.TrimEnd('\r', '\n') + newline + newline + $"[{section}]" + newline + $"{key}={value}" + newline;

        int sectionEnd = text.IndexOf(newline + "[", sectionMatch.Index + sectionMatch.Length, StringComparison.Ordinal);
        if (sectionEnd < 0) sectionEnd = text.Length;
        int contentStart = sectionMatch.Index + sectionMatch.Length;
        string content = text[contentStart..sectionEnd];
        var keyRegex = new Regex($@"(?im)^(\s*){Regex.Escape(key)}\s*=.*$", RegexOptions.CultureInvariant);
        Match keyMatch = keyRegex.Match(content);
        if (keyMatch.Success)
        {
            int absolute = contentStart + keyMatch.Index;
            return text[..absolute] + $"{key}={value}" + text[(absolute + keyMatch.Length)..];
        }
        int insertAt = text.IndexOf('\n', sectionMatch.Index + sectionMatch.Length);
        if (insertAt < 0) return text + newline + $"{key}={value}" + newline;
        return text.Insert(insertAt + 1, $"{key}={value}" + newline);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(true);
        return Encoding.Default;
    }
}
