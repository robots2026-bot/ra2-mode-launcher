using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using lzo.net;

namespace Ra2ModeLauncher;

internal static class MapPreviewExtractor
{
    public static Bitmap? Extract(string path)
    {
        string? size = null;
        var packed = new StringBuilder();
        bool preview = false, previewPack = false;
        foreach (string sourceLine in File.ReadLines(path))
        {
            string line = sourceLine.Split(';')[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1];
                preview = section.Equals("Preview", StringComparison.OrdinalIgnoreCase);
                previewPack = section.Equals("PreviewPack", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals < 0) continue;
            if (preview && line[..equals].Trim().Equals("Size", StringComparison.OrdinalIgnoreCase)) size = line[(equals + 1)..].Trim();
            else if (previewPack) packed.Append(line[(equals + 1)..].Trim());
        }

        string[] dimensions = size?.Split(',') ?? [];
        if (dimensions.Length < 4 || !int.TryParse(dimensions[2], out int width) || !int.TryParse(dimensions[3], out int height) || width <= 0 || height <= 0 || packed.Length == 0) return null;
        try
        {
            byte[] compressed = Convert.FromBase64String(packed.ToString());
            byte[] rgb = Decompress(compressed, checked(width * height * 3));
            return CreateBitmap(width, height, rgb);
        }
        catch { return null; }
    }

    private static byte[] Decompress(byte[] source, int outputSize)
    {
        byte[] output = new byte[outputSize];
        int read = 0, written = 0;
        while (read + 4 <= source.Length)
        {
            ushort compressedSize = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(read)); read += 2;
            ushort uncompressedSize = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(read)); read += 2;
            if (compressedSize == 0 || uncompressedSize == 0) break;
            if (read + compressedSize > source.Length || written + uncompressedSize > output.Length) throw new InvalidDataException();
            using var stream = new LzoStream(new MemoryStream(source, read, compressedSize), CompressionMode.Decompress);
            int remaining = uncompressedSize;
            while (remaining > 0)
            {
                int count = stream.Read(output, written, remaining);
                if (count == 0) throw new EndOfStreamException();
                written += count; remaining -= count;
            }
            read += compressedSize;
        }
        if (written != outputSize) throw new InvalidDataException();
        return output;
    }

    private static Bitmap CreateBitmap(int width, int height, byte[] rgb)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            byte[] row = new byte[data.Stride];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int source = (y * width + x) * 3;
                    int target = x * 3;
                    row[target] = rgb[source + 2]; row[target + 1] = rgb[source + 1]; row[target + 2] = rgb[source];
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}
