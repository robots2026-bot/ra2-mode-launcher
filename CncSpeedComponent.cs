using System.Security.Cryptography;

namespace Ra2ModeLauncher;

internal static class CncSpeedComponent
{
    public static bool IsInstalled(string runtimePath)
    {
        try
        {
            string packaged = Path.Combine(AppContext.BaseDirectory, "native-runtime", "ddraw.dll");
            string installed = Path.Combine(runtimePath, "ddraw.dll");
            if (!File.Exists(packaged) || !File.Exists(installed)) return false;
            using var package = File.OpenRead(packaged);
            using var runtime = File.OpenRead(installed);
            return SHA256.HashData(package).AsSpan().SequenceEqual(SHA256.HashData(runtime));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
