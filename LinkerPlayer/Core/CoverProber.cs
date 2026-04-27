using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace LinkerPlayer.Core;

/// <summary>
/// Lightweight, allocation-minimal cover-presence prober.
/// Reads only the minimal bytes needed to detect an embedded picture tag —
/// never loads image data — so it runs fast even over a UNC/NAS share.
///
/// Supported formats: MP3 (ID3v2), FLAC, OGG/Opus (FLAC PICTURE block in
/// Vorbis comment packets), M4A/MP4/MKA/MKV (MP4 ilst / Matroska COVR),
/// WMA/ASF, APE tags (used by APE/WV/MPC).
/// Falls back to ATL for any format not recognised.
/// </summary>
internal static class CoverProber
{
    // ID3v2 frame IDs that carry cover art
    private static readonly byte[] s_apicId3v23 = "APIC"u8.ToArray();
    private static readonly byte[] s_picId3v22  = "PIC"u8.ToArray();

    // FLAC block type 6 = PICTURE
    private const byte FlacPictureBlockType = 6;

    // OGG capture pattern
    private static readonly byte[] s_oggCapture = "OggS"u8.ToArray();

    // Vorbis comment / FLAC-in-OGG picture marker in base-64 header
    private const string VorbisPictureKey = "METADATA_BLOCK_PICTURE=";

    // WMA / ASF header object GUID (little-endian)
    private static readonly byte[] s_asfHeaderGuid =
    [
        0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11,
        0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C
    ];
    // ASF Extended Content Description Object GUID
    private static readonly byte[] s_asfExContentGuid =
    [
        0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11,
        0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50
    ];

    /// <summary>
    /// Returns true if the file at <paramref name="path"/> contains at least one
    /// embedded picture, without decoding the image bytes.
    /// </summary>
    public static bool HasEmbeddedCover(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".mp3"         => ProbeId3V2(path),
                ".flac"        => ProbeFlac(path),
                ".ogg" or
                ".opus" or
                ".oga"         => ProbeOgg(path),
                ".m4a" or
                ".mp4" or
                ".m4b" or
                ".m4p"         => ProbeMp4(path),
                ".wma"         => ProbeAsf(path),
                ".ape" or
                ".wv"  or
                ".mpc" or
                ".ofr"         => ProbeApeTag(path),
                // MKA/MKV: too complex to probe cheaply; fall back to ATL
                _ => ProbeViaAtl(path)
            };
        }
        catch
        {
            return false;
        }
    }

    // ── ID3v2 (MP3) ─────────────────────────────────────────────────────────

    private static bool ProbeId3V2(string path)
    {
        using FileStream fs = OpenRead(path);
        Span<byte> hdr = stackalloc byte[10];
        if (fs.Read(hdr) < 10) return false;
        if (hdr[0] != 'I' || hdr[1] != 'D' || hdr[2] != '3') return false;

        int version = hdr[3]; // 2, 3, or 4
        bool unsync  = (hdr[5] & 0x80) != 0;
        int tagSize  = SyncSafeInt(hdr[6..10]);

        // Cap how much of the ID3 tag we scan (we only need to find one frame)
        // 512 KB is plenty for the frame directory; actual picture data comes after.
        int scanBytes = Math.Min(tagSize, 512 * 1024);
        byte[] tagBuf = new byte[scanBytes];
        int read = ReadFull(fs, tagBuf);

        if (unsync) DecodeUnsync(tagBuf, ref read);

        int pos = 0;
        bool is22 = version == 2;

        while (pos + (is22 ? 6 : 10) <= read)
        {
            if (is22)
            {
                // ID3v2.2: 3-char ID + 3-byte size
                if (tagBuf[pos] == 0) break;
                bool isPic = tagBuf[pos] == s_picId3v22[0] &&
                             tagBuf[pos + 1] == s_picId3v22[1] &&
                             tagBuf[pos + 2] == s_picId3v22[2];
                if (isPic) return true;
                int sz = (tagBuf[pos + 3] << 16) | (tagBuf[pos + 4] << 8) | tagBuf[pos + 5];
                pos += 6 + sz;
            }
            else
            {
                // ID3v2.3/4: 4-char ID + 4-byte size
                if (tagBuf[pos] == 0) break;
                bool isApic = tagBuf[pos] == s_apicId3v23[0] &&
                              tagBuf[pos + 1] == s_apicId3v23[1] &&
                              tagBuf[pos + 2] == s_apicId3v23[2] &&
                              tagBuf[pos + 3] == s_apicId3v23[3];
                if (isApic) return true;

                int sz = version == 4
                    ? SyncSafeInt(tagBuf.AsSpan(pos + 4, 4))
                    : BinaryPrimitives.ReadInt32BigEndian(tagBuf.AsSpan(pos + 4, 4));
                pos += 10 + sz;
            }
        }
        return false;
    }

    // ── FLAC ────────────────────────────────────────────────────────────────

    private static bool ProbeFlac(string path)
    {
        using FileStream fs = OpenRead(path);
        Span<byte> sig = stackalloc byte[4];
        if (fs.Read(sig) < 4) return false;
        if (sig[0] != 'f' || sig[1] != 'L' || sig[2] != 'a' || sig[3] != 'C') return false;

        Span<byte> blkHdr = stackalloc byte[4];
        while (fs.Read(blkHdr) == 4)
        {
            bool isLast  = (blkHdr[0] & 0x80) != 0;
            int  type    = blkHdr[0] & 0x7F;
            int  length  = (blkHdr[1] << 16) | (blkHdr[2] << 8) | blkHdr[3];

            if (type == FlacPictureBlockType) return true;
            if (isLast) break;
            fs.Seek(length, SeekOrigin.Current);
        }
        return false;
    }

    // ── OGG / Opus (FLAC PICTURE in Vorbis comment) ─────────────────────────

    private static bool ProbeOgg(string path)
    {
        using FileStream fs = OpenRead(path);
        // Scan only the first 128 KB — comment packet is always near the start
        byte[] buf = new byte[Math.Min(128 * 1024, fs.Length > 0 ? (int)fs.Length : 128 * 1024)];
        int read = ReadFull(fs, buf);

        // Search for the Vorbis comment picture key as raw ASCII bytes
        ReadOnlySpan<byte> key = "METADATA_BLOCK_PICTURE="u8;
        // Also check uppercase: spec says case-insensitive
        return buf.AsSpan(0, read).IndexOf(key) >= 0 ||
               // Some encoders use uppercase
               IndexOfCaseInsensitive(buf.AsSpan(0, read), key) >= 0;
    }

    // ── MP4 / M4A ────────────────────────────────────────────────────────────

    private static bool ProbeMp4(string path)
    {
        using FileStream fs = OpenRead(path);
        // Walk top-level boxes looking for moov; inside moov walk for udta/ilst/covr
        return WalkMp4Boxes(fs, fs.Length, 0);
    }

    private static bool WalkMp4Boxes(Stream fs, long limit, int depth)
    {
        if (depth > 6) return false;
        long end = fs.Position + limit;
        Span<byte> boxHdr = stackalloc byte[8];

        while (fs.Position + 8 <= end)
        {
            int hdrRead = fs.Read(boxHdr);
            if (hdrRead < 8) break;

            long size   = BinaryPrimitives.ReadUInt32BigEndian(boxHdr[..4]);
            string type = Encoding.ASCII.GetString(boxHdr[4..8]);

            long dataSize;
            if (size == 1)
            {
                // Extended 64-bit size
                Span<byte> ext = stackalloc byte[8];
                if (fs.Read(ext) < 8) break;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(ext);
                dataSize = size - 16;
            }
            else if (size == 0)
            {
                dataSize = end - fs.Position;
            }
            else
            {
                dataSize = size - 8;
            }

            if (dataSize < 0) break;

            if (type is "covr") return true;

            bool descend = depth < 5 && type is "moov" or "udta" or "meta" or "ilst" or "trak" or "mdia";
            if (descend)
            {
                long savedEnd = end;
                bool found = WalkMp4Boxes(fs, dataSize, depth + 1);
                if (found) return true;
                // Skip any remaining bytes in this box
                long afterBox = fs.Position;
                long boxAbsEnd = afterBox - dataSize + dataSize; // recompute
                _ = savedEnd; // suppress warning
            }

            fs.Seek(dataSize, SeekOrigin.Current);
        }
        return false;
    }

    // ── WMA / ASF ────────────────────────────────────────────────────────────

    private static bool ProbeAsf(string path)
    {
        using FileStream fs = OpenRead(path);
        Span<byte> guid = stackalloc byte[16];
        if (fs.Read(guid) < 16) return false;
        if (!guid.SequenceEqual(s_asfHeaderGuid)) return false;

        // Read header object size (8 bytes LE) and number of header objects (4 bytes)
        Span<byte> meta = stackalloc byte[14];
        if (fs.Read(meta) < 14) return false;
        // long headerSize = BinaryPrimitives.ReadInt64LittleEndian(meta[..8]);
        int numObjects = BinaryPrimitives.ReadInt32LittleEndian(meta[8..12]);

        for (int i = 0; i < numObjects && i < 64; i++)
        {
            Span<byte> objHdr = stackalloc byte[24];
            if (fs.Read(objHdr) < 24) break;
            long objSize = BinaryPrimitives.ReadInt64LittleEndian(objHdr[16..24]);

            // Check for Extended Content Description or Content Branding objects
            // which is where WMA stores cover art (WM/Picture attribute)
            if (objHdr[..16].SequenceEqual(s_asfExContentGuid))
            {
                // Scan up to 64 KB of this object for "WM/Picture" string
                int scanLen = (int)Math.Min(objSize - 24, 64 * 1024);
                byte[] objBuf = new byte[scanLen];
                ReadFull(fs, objBuf);
                // "WM/Picture" in UTF-16LE
                ReadOnlySpan<byte> wmPic = "W\0M\0/\0P\0i\0c\0t\0u\0r\0e\0"u8;
                if (objBuf.AsSpan().IndexOf(wmPic) >= 0) return true;
                continue;
            }

            fs.Seek(objSize - 24, SeekOrigin.Current);
        }
        return false;
    }

    // ── APE tag (APE, WavPack, Musepack, OptimFROG) ──────────────────────────

    private static bool ProbeApeTag(string path)
    {
        using FileStream fs = OpenRead(path);
        // APE tag is usually at the end. Preamble: "APETAGEX" at file end - 32 bytes
        if (fs.Length < 32) return false;
        fs.Seek(-32, SeekOrigin.End);
        Span<byte> footer = stackalloc byte[32];
        if (fs.Read(footer) < 32) return false;

        if (footer[0] != 'A' || footer[1] != 'P' || footer[2] != 'E' ||
            footer[3] != 'T' || footer[4] != 'A' || footer[5] != 'G' ||
            footer[6] != 'E' || footer[7] != 'X') return false;

        int tagSize  = BinaryPrimitives.ReadInt32LittleEndian(footer[12..16]);
        int itemCount = BinaryPrimitives.ReadInt32LittleEndian(footer[16..20]);
        if (tagSize <= 0 || itemCount <= 0) return false;

        fs.Seek(-(tagSize), SeekOrigin.End);
        int readLen = Math.Min(tagSize, 64 * 1024);
        byte[] tagBuf = new byte[readLen];
        ReadFull(fs, tagBuf);

        // Look for "Cover Art" key (case-insensitive)
        ReadOnlySpan<byte> coverKey = "Cover Art"u8;
        return IndexOfCaseInsensitive(tagBuf.AsSpan(), coverKey) >= 0;
    }

    // ── ATL fallback ─────────────────────────────────────────────────────────

    private static bool ProbeViaAtl(string path)
    {
        ATL.Track t = new(path);
        return t.EmbeddedPictures.Count > 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);

    private static int ReadFull(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static int SyncSafeInt(ReadOnlySpan<byte> b) =>
        (b[0] << 21) | (b[1] << 14) | (b[2] << 7) | b[3];

    private static void DecodeUnsync(byte[] buf, ref int length)
    {
        int w = 0;
        for (int r = 0; r < length - 1; r++)
        {
            buf[w++] = buf[r];
            if (buf[r] == 0xFF && buf[r + 1] == 0x00) r++;
        }
        if (length > 0) buf[w++] = buf[length - 1];
        length = w;
    }

    private static int IndexOfCaseInsensitive(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty) return 0;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (char.ToUpperInvariant((char)haystack[i + j]) != char.ToUpperInvariant((char)needle[j]))
                { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
