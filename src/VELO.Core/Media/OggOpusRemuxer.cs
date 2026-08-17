namespace VELO.Core.Media;

/// <summary>Outcome of a remux. <paramref name="Error"/> is null on success.</summary>
public sealed record RemuxResult(bool Success, long BytesWritten, int Packets, string? Error)
{
    public static RemuxResult Fail(string error) => new(false, 0, 0, error);
}

/// <summary>
/// Rewraps Opus audio from a WebM/Matroska container into Ogg, losslessly.
///
/// Why this exists instead of ffmpeg: the MSE capture yields Opus-in-WebM
/// (measured — <c>Track CodecId=A_OPUS</c>), and many players dispatch on the
/// extension and treat <c>.webm</c> as video, so a perfectly good audio file
/// gets rejected. Fixing that does not need a transcoder, only a different
/// envelope: the Opus packets are already exactly what an Ogg-Opus file
/// carries, and the <c>OpusHead</c> that Ogg wants is already sitting in the
/// WebM's CodecPrivate — 19 bytes, which is what an OpusHead is.
///
/// So this copies packets and rewrites framing. Not a single audio byte is
/// re-encoded, which is why the result is bit-identical audio rather than a
/// second lossy generation. D-1's ffmpeg download stays deferred for the jobs
/// that genuinely need a codec: MP3, and muxing audio with video.
///
/// Deliberately narrow. It handles the one case the capture produces and
/// refuses everything else with a reason, rather than guessing and emitting a
/// file that plays as noise.
/// </summary>
public static class OggOpusRemuxer
{
    // ── EBML element ids, stored with their marker bits as they appear ────
    private const uint IdSegment      = 0x18538067;
    private const uint IdTracks       = 0x1654AE6B;
    private const uint IdTrackEntry   = 0xAE;
    private const uint IdTrackNumber  = 0xD7;
    private const uint IdTrackType    = 0x83;
    private const uint IdCodecId      = 0x86;
    private const uint IdCodecPrivate = 0x63A2;
    private const uint IdCluster      = 0x1F43B675;
    private const uint IdSimpleBlock  = 0xA3;
    private const uint IdBlockGroup   = 0xA0;
    private const uint IdBlock        = 0xA1;

    private const int OpusSampleRate = 48000;

    /// <summary>
    /// Reads a WebM byte array and writes an Ogg-Opus stream.
    /// Never throws for bad input — a truncated or unexpected file is an
    /// answer, not an exception, because this runs off a UI action.
    /// </summary>
    public static RemuxResult ToOggOpus(byte[] webm, Stream output)
    {
        try
        {
            return Run(webm, output);
        }
        catch (Exception ex)
        {
            return RemuxResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RemuxResult Run(byte[] d, Stream output)
    {
        if (d.Length < 4 || d[0] != 0x1A || d[1] != 0x45 || d[2] != 0xDF || d[3] != 0xA3)
            return RemuxResult.Fail("Not a Matroska/WebM file (no EBML header).");

        byte[]? opusHead = null;
        long    opusTrack = -1;
        var     packets = new List<byte[]>();
        // Distinguishes "the track list had no Opus in it" from "the file put
        // clusters before its track list". Without this the first case reports
        // the second, which sends the reader looking at container layout when
        // the real answer is that the audio is Vorbis.
        var     sawTracks = false;

        // Top level: walk to Segment, then walk its children. Everything that
        // is not Tracks or Cluster is skipped by size, which is what keeps
        // this a reader rather than a parser.
        var pos = 0;
        while (pos < d.Length)
        {
            if (!ReadElement(d, ref pos, out var id, out var size, out var dataStart)) break;

            if (id == IdSegment)
            {
                // Descend rather than skip.
                pos = dataStart;
                continue;
            }

            if (id == IdTracks)
            {
                sawTracks = true;
                ParseTracks(d, dataStart, ClampEnd(d, dataStart, size), ref opusTrack, ref opusHead);
                pos = SkipTo(d, dataStart, size);
                continue;
            }

            if (id == IdCluster)
            {
                // Nothing to collect: fall through to the final check, which
                // says "no Opus track" — the accurate answer for a file whose
                // audio is some other codec.
                if (opusTrack < 0 && sawTracks)
                {
                    pos = SkipTo(d, dataStart, size);
                    continue;
                }

                if (opusTrack < 0)
                    return RemuxResult.Fail("Clusters appear before the track list; unsupported layout.");

                var err = ParseCluster(d, dataStart, ClampEnd(d, dataStart, size), opusTrack, packets);
                if (err is not null) return RemuxResult.Fail(err);

                pos = SkipTo(d, dataStart, size);
                continue;
            }

            pos = SkipTo(d, dataStart, size);
        }

        if (opusHead is null)
            return RemuxResult.Fail("No Opus track found in this file.");
        if (packets.Count == 0)
            return RemuxResult.Fail("The Opus track carries no audio frames.");

        WriteOgg(output, opusHead, packets, out var written);
        return new RemuxResult(true, written, packets.Count, null);
    }

    // ── Matroska ──────────────────────────────────────────────────────────

    private static void ParseTracks(byte[] d, int start, int end, ref long trackNumber, ref byte[]? codecPrivate)
    {
        var pos = start;
        while (pos < end)
        {
            if (!ReadElement(d, ref pos, out var id, out var size, out var dataStart)) return;

            if (id == IdTrackEntry)
            {
                var entryEnd = ClampEnd(d, dataStart, size);
                long  number = -1;
                long  type   = -1;
                string codec = "";
                byte[]? priv = null;

                var p = dataStart;
                while (p < entryEnd)
                {
                    if (!ReadElement(d, ref p, out var cid, out var csize, out var cstart)) break;
                    var cend = ClampEnd(d, cstart, csize);

                    if (cid == IdTrackNumber)  number = ReadUInt(d, cstart, cend);
                    else if (cid == IdTrackType) type  = ReadUInt(d, cstart, cend);
                    else if (cid == IdCodecId)   codec = System.Text.Encoding.ASCII
                                                            .GetString(d, cstart, cend - cstart).TrimEnd('\0');
                    else if (cid == IdCodecPrivate) priv = d[cstart..cend];

                    p = cend;
                }

                // TrackType 2 is audio. Checking the codec id as well because a
                // file can carry several audio tracks and only Opus is in scope.
                if (type == 2 && codec.StartsWith("A_OPUS", StringComparison.OrdinalIgnoreCase) && priv is not null)
                {
                    trackNumber  = number;
                    codecPrivate = priv;
                }
            }

            pos = SkipTo(d, dataStart, size);
        }
    }

    private static string? ParseCluster(byte[] d, int start, int end, long wantTrack, List<byte[]> packets)
    {
        var pos = start;
        while (pos < end)
        {
            if (!ReadElement(d, ref pos, out var id, out var size, out var dataStart)) return null;
            var elemEnd = ClampEnd(d, dataStart, size);

            if (id == IdSimpleBlock)
            {
                var err = ReadBlockFrames(d, dataStart, elemEnd, wantTrack, packets);
                if (err is not null) return err;
            }
            else if (id == IdBlockGroup)
            {
                var p = dataStart;
                while (p < elemEnd)
                {
                    if (!ReadElement(d, ref p, out var bid, out var bsize, out var bstart)) break;
                    var bend = ClampEnd(d, bstart, bsize);
                    if (bid == IdBlock)
                    {
                        var err = ReadBlockFrames(d, bstart, bend, wantTrack, packets);
                        if (err is not null) return err;
                    }
                    p = bend;
                }
            }

            pos = elemEnd;
        }
        return null;
    }

    /// <summary>
    /// A (Simple)Block is: track number as a VINT, a 2-byte signed timecode, a
    /// flags byte, then the frames. Only the frames matter here — Ogg granule
    /// positions are rebuilt from Opus packet durations, which is both simpler
    /// and more robust than trusting cluster timestamps.
    /// </summary>
    private static string? ReadBlockFrames(byte[] d, int start, int end, long wantTrack, List<byte[]> packets)
    {
        var pos = start;
        var track = ReadVInt(d, ref pos, stripMarker: true);
        if (track < 0 || pos + 3 > end) return null;

        pos += 2;                       // timecode, not needed
        var flags = d[pos++];
        if (track != wantTrack) return null;

        var lacing = (flags >> 1) & 0x03;

        if (lacing == 0)
        {
            if (end > pos) packets.Add(d[pos..end]);
            return null;
        }

        if (pos >= end) return null;
        int frameCount = d[pos++] + 1;

        if (lacing == 2)                // fixed-size
        {
            var total = end - pos;
            if (frameCount <= 0 || total % frameCount != 0)
                return "Fixed-size lacing with an inconsistent frame count.";
            var each = total / frameCount;
            for (var i = 0; i < frameCount; i++)
                packets.Add(d[(pos + i * each)..(pos + (i + 1) * each)]);
            return null;
        }

        // Xiph (1) and EBML (3) lacing. The capture has never produced either
        // — Opus in WebM is written one frame per block — and guessing here
        // would emit a file that plays as noise, which is worse than refusing.
        return "This file uses Xiph or EBML lacing, which VELO does not read yet.";
    }

    // ── EBML primitives ───────────────────────────────────────────────────

    private static bool ReadElement(byte[] d, ref int pos, out uint id, out long size, out int dataStart)
    {
        id = 0; size = 0; dataStart = pos;
        if (pos >= d.Length) return false;

        var idLen = LeadingLength(d[pos]);
        if (idLen == 0 || pos + idLen > d.Length) return false;

        // Element ids keep their marker bits; they are used as written.
        uint value = 0;
        for (var i = 0; i < idLen; i++) value = (value << 8) | d[pos + i];
        id = value;
        pos += idLen;

        size = ReadVInt(d, ref pos, stripMarker: true);
        dataStart = pos;
        return size >= -1;
    }

    private static long ReadVInt(byte[] d, ref int pos, bool stripMarker)
    {
        if (pos >= d.Length) return -1;
        var len = LeadingLength(d[pos]);
        if (len == 0 || pos + len > d.Length) return -1;

        long value = stripMarker ? d[pos] & ((1 << (8 - len)) - 1) : d[pos];
        var allOnes = value == ((1 << (8 - len)) - 1);

        for (var i = 1; i < len; i++)
        {
            value = (value << 8) | d[pos + i];
            if (d[pos + i] != 0xFF) allOnes = false;
        }
        pos += len;

        // An all-ones VINT means "unknown size" — legal for live-written
        // Segments and Clusters. Reported as -1 so callers read to the end
        // rather than trusting a nonsense length.
        return allOnes ? -1 : value;
    }

    private static int LeadingLength(byte first)
    {
        for (var i = 0; i < 8; i++)
            if ((first & (0x80 >> i)) != 0) return i + 1;
        return 0;
    }

    /// <summary>End offset of an element, clamped to the buffer. Unknown size
    /// (-1) means "to the end", which is how a stream-written file appears.</summary>
    private static int ClampEnd(byte[] d, int dataStart, long size)
        => size < 0 ? d.Length : (int)Math.Min(d.Length, dataStart + size);

    private static int SkipTo(byte[] d, int dataStart, long size)
        => size < 0 ? d.Length : (int)Math.Min(d.Length, dataStart + size);

    private static long ReadUInt(byte[] d, int start, int end)
    {
        long v = 0;
        for (var i = start; i < end && i < d.Length; i++) v = (v << 8) | d[i];
        return v;
    }

    // ── Opus packet duration ──────────────────────────────────────────────

    /// <summary>
    /// Samples at 48 kHz carried by one Opus packet, read from its TOC byte
    /// (RFC 6716 §3.1). Needed because Ogg granule positions are sample
    /// counts, so they have to be derived rather than copied.
    /// </summary>
    internal static int PacketSamples(byte[] packet)
    {
        if (packet.Length == 0) return 0;

        var toc    = packet[0];
        var config = toc >> 3;
        var code   = toc & 0x03;

        var frameSamples = config switch
        {
            < 12 => (config & 0x03) switch { 0 => 480, 1 => 960, 2 => 1920, _ => 2880 },
            < 16 => (config & 0x01) == 0 ? 480 : 960,
            _    => (config & 0x03) switch { 0 => 120, 1 => 240, 2 => 480, _ => 960 },
        };

        var frames = code switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length >= 2 ? packet[1] & 0x3F : 0,
        };

        return frameSamples * frames;
    }

    // ── Ogg ───────────────────────────────────────────────────────────────

    private static void WriteOgg(Stream output, byte[] opusHead, List<byte[]> packets, out long written)
    {
        // A fixed serial keeps output deterministic, which is what makes the
        // remuxer testable byte-for-byte. Ogg only requires the serial to be
        // unique among concurrently multiplexed streams, and there is exactly
        // one stream here.
        const uint serial = 0x56454C4F;   // "VELO"
        uint seq = 0;
        long granule = 0;
        var counter = new CountingStream(output);

        WritePage(counter, serial, ref seq, 0x02, 0, [opusHead]);      // BOS
        WritePage(counter, serial, ref seq, 0x00, 0, [BuildOpusTags()]);

        var batch = new List<byte[]>();
        var segments = 0;

        foreach (var p in packets)
        {
            var needed = p.Length / 255 + 1;

            // 255 segments is the hard per-page limit in the Ogg format.
            if (segments + needed > 255)
            {
                WritePage(counter, serial, ref seq, 0x00, granule, batch);
                batch.Clear();
                segments = 0;
            }

            batch.Add(p);
            segments += needed;
            granule += PacketSamples(p);
        }

        // The final page carries EOS. Written even when the batch is empty so
        // the stream is always terminated — a player reading an unterminated
        // Ogg reports the file as truncated.
        WritePage(counter, serial, ref seq, 0x04, granule, batch);

        written = counter.Written;
    }

    private static byte[] BuildOpusTags()
    {
        var vendor = System.Text.Encoding.UTF8.GetBytes("VELO");
        var buf = new MemoryStream();
        buf.Write("OpusTags"u8);
        buf.Write(BitConverter.GetBytes(vendor.Length));
        buf.Write(vendor);
        buf.Write(BitConverter.GetBytes(0));   // no user comments
        return buf.ToArray();
    }

    private static void WritePage(
        Stream output, uint serial, ref uint seq, byte headerType, long granule, List<byte[]> packets)
    {
        var lacing = new List<byte>();
        foreach (var p in packets)
        {
            var remaining = p.Length;
            while (remaining >= 255) { lacing.Add(255); remaining -= 255; }
            // A packet whose length is a multiple of 255 needs a trailing zero
            // segment, otherwise a reader cannot tell it ended.
            lacing.Add((byte)remaining);
        }

        var header = new byte[27 + lacing.Count];
        header[0] = (byte)'O'; header[1] = (byte)'g'; header[2] = (byte)'g'; header[3] = (byte)'S';
        header[4] = 0;
        header[5] = headerType;
        BitConverter.GetBytes(granule).CopyTo(header, 6);
        BitConverter.GetBytes(serial).CopyTo(header, 14);
        BitConverter.GetBytes(seq).CopyTo(header, 18);
        // CRC field (22..25) stays zero while the checksum is computed.
        header[26] = (byte)lacing.Count;
        lacing.CopyTo(header, 27);

        var crc = OggCrc(header, 0, header.Length, 0);
        foreach (var p in packets) crc = OggCrc(p, 0, p.Length, crc);
        BitConverter.GetBytes(crc).CopyTo(header, 22);

        output.Write(header, 0, header.Length);
        foreach (var p in packets) output.Write(p, 0, p.Length);

        seq++;
    }

    /// <summary>
    /// Ogg's CRC32: polynomial 0x04C11DB7, initial value zero, and — unlike
    /// almost every other CRC32 — no input or output reflection and no final
    /// xor. Getting this wrong produces a file every player rejects, with no
    /// hint as to why.
    /// </summary>
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var r = i << 24;
            for (var j = 0; j < 8; j++)
                r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
            table[i] = r;
        }
        return table;
    }

    private static uint OggCrc(byte[] data, int offset, int count, uint crc)
    {
        for (var i = offset; i < offset + count; i++)
            crc = (crc << 8) ^ CrcTable[((crc >> 24) & 0xFF) ^ data[i]];
        return crc;
    }

    /// <summary>Counts bytes written without buffering the output.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Written { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Written += count;
        }

        public override bool CanRead  => false;
        public override bool CanSeek  => false;
        public override bool CanWrite => true;
        public override long Length   => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
