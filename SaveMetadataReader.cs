using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Ra2ModeLauncher;

internal static class SaveMetadataReader
{
    private const uint StgmReadShareExclusive = 0x10;
    private const uint VtLpstr = 30;
    private const uint VtLpwstr = 31;

    public static string GetDisplayName(string path)
    {
        string fallback = Path.GetFileNameWithoutExtension(path);
        try
        {
            Marshal.ThrowExceptionForHR(StgOpenStorage(path, null, StgmReadShareExclusive, IntPtr.Zero, 0, out IStorage storage));
            try
            {
                storage.OpenStream("\u0005SummaryInformation", IntPtr.Zero, StgmReadShareExclusive, 0, out IStream stream);
                byte[] data = ReadAll(stream);
                string? title = ReadPropertyString(data, 2);
                title = title?.Trim().TrimEnd('\0');
                return string.IsNullOrWhiteSpace(title) ? fallback : title;
            }
            finally
            {
                Marshal.ReleaseComObject(storage);
            }
        }
        catch
        {
            return fallback;
        }
    }

    public static bool UsesAresExtensions(string path)
    {
        try
        {
            // The save summary written by Ares identifies the executable as
            // "GAMEMD.EXE + Ares/... + Phobos ..." in a UTF-16 property.
            string content = Encoding.Unicode.GetString(File.ReadAllBytes(path));
            return content.Contains(" + Ares/", StringComparison.OrdinalIgnoreCase)
                || content.Contains(" + Phobos ", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadAll(IStream stream)
    {
        stream.Stat(out STATSTG stat, 1);
        if (stat.cbSize < 0 || stat.cbSize > 1024 * 1024) throw new InvalidDataException("存档摘要大小异常。");
        byte[] data = new byte[(int)stat.cbSize];
        IntPtr count = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            stream.Read(data, data.Length, count);
            int read = Marshal.ReadInt32(count);
            if (read != data.Length) Array.Resize(ref data, read);
            return data;
        }
        finally
        {
            Marshal.FreeCoTaskMem(count);
            Marshal.ReleaseComObject(stream);
        }
    }

    private static string? ReadPropertyString(byte[] data, uint propertyId)
    {
        if (data.Length < 48 || BitConverter.ToUInt16(data, 0) != 0xFFFE) return null;
        uint sectionCount = BitConverter.ToUInt32(data, 24);
        if (sectionCount == 0) return null;
        int section = checked((int)BitConverter.ToUInt32(data, 44));
        if (section < 0 || section + 8 > data.Length) return null;
        int propertyCount = checked((int)BitConverter.ToUInt32(data, section + 4));
        for (int i = 0; i < propertyCount; i++)
        {
            int entry = section + 8 + i * 8;
            if (entry + 8 > data.Length || BitConverter.ToUInt32(data, entry) != propertyId) continue;
            int value = checked(section + (int)BitConverter.ToUInt32(data, entry + 4));
            if (value + 8 > data.Length) return null;
            uint type = BitConverter.ToUInt32(data, value);
            int length = checked((int)BitConverter.ToUInt32(data, value + 4));
            if (type == VtLpstr && length > 0 && value + 8 + length <= data.Length)
                return Encoding.Default.GetString(data, value + 8, length - 1);
            if (type == VtLpwstr && length > 0 && value + 8 + length * 2 <= data.Length)
                return Encoding.Unicode.GetString(data, value + 8, (length - 1) * 2);
            return null;
        }
        return null;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgOpenStorage(string pwcsName, IStorage? pstgPriority, uint grfMode, IntPtr snbExclude, uint reserved, out IStorage ppstgOpen);

    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStream stream);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved1, uint mode, uint reserved2, out IStream stream);
    }
}
