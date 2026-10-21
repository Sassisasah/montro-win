using System.Buffers.Binary;

namespace Montro.Pptx;

/// <summary>
/// Länge eines Videos ohne Fremdbibliothek: liest bei MP4/MOV/M4V (ISO-BMFF, das übliche Format
/// in PowerPoint) die Dauer aus dem Kopf <c>moov/mvhd</c>. Andere Formate liefern 0 = unbekannt.
/// Am Mac übernimmt das AVFoundation.
/// </summary>
public static class MediaDuration
{
    public static double Seconds(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            long moov = FindBox(fs, 0, fs.Length, "moov");
            if (moov < 0) return 0;
            fs.Position = moov;
            var (size, header) = ReadBoxHeader(fs);
            long mvhd = FindBox(fs, moov + header, moov + size, "mvhd");
            if (mvhd < 0) return 0;
            fs.Position = mvhd;
            (_, header) = ReadBoxHeader(fs);
            fs.Position = mvhd + header;
            Span<byte> b = stackalloc byte[32];
            if (fs.Read(b) < 32) return 0;
            int version = b[0];
            uint timescale;
            ulong duration;
            if (version == 1)
            {
                timescale = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(20, 4));
                duration = BinaryPrimitives.ReadUInt64BigEndian(b.Slice(24, 8));
            }
            else
            {
                timescale = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(12, 4));
                duration = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(16, 4));
            }
            if (timescale == 0 || duration == 0 || duration == uint.MaxValue) return 0;
            return (double)duration / timescale;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Position der ersten Box dieses Typs zwischen start und end, -1 = keine.</summary>
    private static long FindBox(Stream s, long start, long end, string type)
    {
        long pos = start;
        while (pos + 8 <= end)
        {
            s.Position = pos;
            var (size, _) = ReadBoxHeader(s, out var t);
            if (t == type) return pos;
            if (size <= 0) return -1;
            pos += size;
        }
        return -1;
    }

    private static (long Size, int Header) ReadBoxHeader(Stream s) => ReadBoxHeader(s, out _);

    private static (long Size, int Header) ReadBoxHeader(Stream s, out string type)
    {
        Span<byte> h = stackalloc byte[16];
        type = "";
        if (s.Read(h[..8]) < 8) return (0, 8);
        long size = BinaryPrimitives.ReadUInt32BigEndian(h[..4]);
        type = System.Text.Encoding.ASCII.GetString(h.Slice(4, 4));
        if (size == 1)
        {
            if (s.Read(h.Slice(8, 8)) < 8) return (0, 16);
            return ((long)BinaryPrimitives.ReadUInt64BigEndian(h.Slice(8, 8)), 16);
        }
        if (size == 0) size = s.Length - (s.Position - 8);   // bis zum Dateiende
        return (size, 8);
    }
}
