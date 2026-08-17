namespace VELO.Core.Media;

/// <summary>
/// Turns a fragmented MP4 into an ordinary one, losslessly.
///
/// Why: the MSE capture receives fMP4 — an <c>ftyp</c> branded <c>dash</c>, a
/// <c>moov</c> whose sample tables are EMPTY, and then a run of
/// <c>moof</c>+<c>mdat</c> fragments carrying the real timing. Software players
/// rebuild the index on the fly; car head units and other fixed-function
/// decoders do not. Measured on a 2014 Uconnect: the file was listed by name
/// and played nothing, and VLC explained why — <c>track[Id 0x1] read 0
/// samples</c>.
///
/// So this rebuilds what the fragments imply: one <c>stbl</c> with real
/// <c>stts</c>/<c>stsz</c>/<c>stsc</c>/<c>stco</c> tables, a single contiguous
/// <c>mdat</c>, and <c>mvex</c> dropped so nothing still claims the file is
/// fragmented. The audio samples are copied byte for byte — same AAC, new
/// index — which is why this is a rewrap and not a transcode.
///
/// <b>moov is written before mdat</b> on purpose. Hardware decoders often
/// cannot seek backwards to find an index at the end of the file, and a
/// trailing moov is a common reason a technically valid MP4 refuses to play on
/// exactly the devices this exists for.
/// </summary>
public static class Mp4Flattener
{
    /// <summary>Container boxes worth descending into; everything else is a leaf.</summary>
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    { "moov", "trak", "mdia", "minf", "stbl", "edts", "moof", "traf", "mvex", "udta" };

    private sealed class Box
    {
        public required string Type { get; init; }

        /// <summary>
        /// Where this box started in the SOURCE file. Only meaningful while
        /// reading, and only actually needed for <c>moof</c>: a fragment's
        /// data offsets are relative to the start of its own moof unless a
        /// base_data_offset says otherwise.
        /// </summary>
        public int SourceOffset { get; init; }

        public byte[] Data { get; set; } = [];
        public List<Box> Children { get; } = [];
        public bool IsContainer => Containers.Contains(Type);

        public long Size => 8 + (IsContainer ? Children.Sum(c => c.Size) : Data.Length);

        public void Write(Stream s)
        {
            s.Write(BeInt32((int)Size));
            s.Write(System.Text.Encoding.ASCII.GetBytes(Type));
            if (IsContainer) foreach (var c in Children) c.Write(s);
            else s.Write(Data);
        }

        public Box? Find(string path)
        {
            var box = this;
            foreach (var part in path.Split('/'))
            {
                box = box.Children.FirstOrDefault(c => c.Type == part);
                if (box is null) return null;
            }
            return box;
        }
    }

    private readonly record struct Sample(int Offset, int Size, uint Duration);

    /// <summary>
    /// Reads a fragmented MP4 and writes a plain one. Never throws: this runs
    /// off a UI action and a malformed capture is an answer, not a crash.
    /// </summary>
    public static RemuxResult ToPlainMp4(byte[] input, Stream output)
    {
        try
        {
            return Run(input, output);
        }
        catch (Exception ex)
        {
            return RemuxResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static RemuxResult Run(byte[] d, Stream output)
    {
        var top = ParseBoxes(d, 0, d.Length);

        var ftyp = top.FirstOrDefault(b => b.Type == "ftyp");
        var moov = top.FirstOrDefault(b => b.Type == "moov");
        if (moov is null) return RemuxResult.Fail("No moov box; this is not an MP4.");

        var trak = moov.Children.FirstOrDefault(b => b.Type == "trak");
        if (trak is null) return RemuxResult.Fail("The MP4 has no track.");

        var stbl = trak.Find("mdia/minf/stbl");
        var stsd = stbl?.Find("stsd");
        var mdhd = trak.Find("mdia/mdhd");
        if (stbl is null || stsd is null || mdhd is null)
            return RemuxResult.Fail("The track is missing its sample description; unsupported layout.");

        // Collect every sample the fragments describe, in order.
        var samples = new List<Sample>();
        foreach (var moof in top.Where(b => b.Type == "moof"))
        {
            var err = ReadFragment(d, moof, samples);
            if (err is not null) return RemuxResult.Fail(err);
        }

        if (samples.Count == 0)
            return RemuxResult.Fail(
                "No fragments found — this file is already a plain MP4, or it carries no audio.");

        // ── Rebuild the sample tables ────────────────────────────────────
        stbl.Children.RemoveAll(c => c.Type is "stts" or "stsc" or "stsz" or "stco" or "co64" or "stss");
        stbl.Children.Add(BuildStts(samples));
        stbl.Children.Add(BuildStsz(samples));
        stbl.Children.Add(BuildStsc(samples.Count));
        var stco = BuildStco(0);
        stbl.Children.Add(stco);

        // mvex is what declares "this file is fragmented". Leaving it behind
        // makes a demuxer look for fragments that no longer exist.
        moov.Children.RemoveAll(c => c.Type == "mvex");

        // ── Durations, which the init segment leaves at zero ─────────────
        long totalDuration = samples.Sum(s => (long)s.Duration);
        var mediaTimescale = PatchMdhd(mdhd, totalDuration);

        var mvhd = moov.Find("mvhd");
        var movieTimescale = mvhd is null ? mediaTimescale : ReadTimescale(mvhd);
        var movieDuration = mediaTimescale == 0
            ? 0
            : totalDuration * movieTimescale / mediaTimescale;

        if (mvhd is not null) PatchDuration(mvhd, movieDuration, isMvhd: true);
        var tkhd = trak.Find("tkhd");
        if (tkhd is not null) PatchDuration(tkhd, movieDuration, isMvhd: false);

        // ── Lay the file out ─────────────────────────────────────────────
        // The chunk offset depends on the size of moov, and moov contains the
        // chunk offset. It only closes because there is exactly one chunk, so
        // filling the value in afterwards cannot change any box's size.
        // The capture arrives branded `dash`, which announces a fragmented
        // streaming file. Leaving it on a file that is no longer fragmented
        // invites a strict decoder to go looking for fragments and give up.
        // Rebranded as plain audio: M4A, compatible with mp42/isom.
        ftyp ??= new Box { Type = "ftyp" };
        ftyp.Data = BuildAudioFtyp();

        var ftypSize = ftyp.Size;
        var moovSize = moov.Size;
        var mdatPayloadOffset = ftypSize + moovSize + 8;

        stco.Data = BuildStco((int)mdatPayloadOffset).Data;

        using var ms = new MemoryStream();
        ftyp.Write(ms);
        moov.Write(ms);

        var mdatSize = samples.Sum(s => (long)s.Size) + 8;
        ms.Write(BeInt32((int)mdatSize));
        ms.Write("mdat"u8);
        foreach (var s in samples) ms.Write(d, s.Offset, s.Size);

        var bytes = ms.ToArray();
        output.Write(bytes, 0, bytes.Length);

        return new RemuxResult(true, bytes.Length, samples.Count, null);
    }

    // ── Fragments ─────────────────────────────────────────────────────────

    private static string? ReadFragment(byte[] d, Box moof, List<Sample> samples)
    {
        var traf = moof.Children.FirstOrDefault(b => b.Type == "traf");
        if (traf is null) return null;

        var tfhd = traf.Find("tfhd");
        if (tfhd is null) return "A fragment has no track header (tfhd).";

        // tfhd: version+flags, track_id, then optional fields in flag order.
        var f = tfhd.Data;
        var flags = (f[1] << 16) | (f[2] << 8) | f[3];
        var p = 8;                                            // past version/flags + track_id
        long baseDataOffset = moof.SourceOffset;              // default: start of the moof
        if ((flags & 0x000001) != 0) { baseDataOffset = (long)BeUInt64(f, p); p += 8; }
        if ((flags & 0x000002) != 0) { p += 4; }              // sample_description_index
        uint defaultDuration = 0, defaultSize = 0;
        if ((flags & 0x000008) != 0) { defaultDuration = BeUInt32(f, p); p += 4; }
        if ((flags & 0x000010) != 0) { defaultSize = BeUInt32(f, p); p += 4; }

        foreach (var trun in traf.Children.Where(b => b.Type == "trun"))
        {
            var t = trun.Data;
            var tflags = (t[1] << 16) | (t[2] << 8) | t[3];
            var count = (int)BeUInt32(t, 4);
            var q = 8;

            var offset = baseDataOffset;
            if ((tflags & 0x000001) != 0) { offset += (int)BeUInt32(t, q); q += 4; }
            if ((tflags & 0x000004) != 0) { q += 4; }         // first_sample_flags

            for (var i = 0; i < count; i++)
            {
                var duration = defaultDuration;
                var size     = defaultSize;

                if ((tflags & 0x000100) != 0) { duration = BeUInt32(t, q); q += 4; }
                if ((tflags & 0x000200) != 0) { size     = BeUInt32(t, q); q += 4; }
                if ((tflags & 0x000400) != 0) { q += 4; }     // sample_flags
                if ((tflags & 0x000800) != 0) { q += 4; }     // composition offset

                if (size == 0) return "A fragment declares samples with no size.";
                if (offset < 0 || offset + size > d.Length)
                    return "A fragment points outside the file; the capture is truncated.";

                samples.Add(new Sample((int)offset, (int)size, duration));
                offset += size;
            }
        }

        return null;
    }

    // ── Table builders ────────────────────────────────────────────────────

    /// <summary>
    /// Run-length encodes the durations. Audio is almost always one run, but
    /// the last sample of a stream often differs, and a single-entry table
    /// would then misreport the length of every file.
    /// </summary>
    private static Box BuildStts(List<Sample> samples)
    {
        var runs = new List<(uint Count, uint Delta)>();
        foreach (var s in samples)
        {
            if (runs.Count > 0 && runs[^1].Delta == s.Duration)
                runs[^1] = (runs[^1].Count + 1, s.Duration);
            else
                runs.Add((1, s.Duration));
        }

        var data = new MemoryStream();
        data.Write(BeInt32(0));                 // version + flags
        data.Write(BeInt32(runs.Count));
        foreach (var (count, delta) in runs)
        {
            data.Write(BeInt32((int)count));
            data.Write(BeInt32((int)delta));
        }
        return new Box { Type = "stts", Data = data.ToArray() };
    }

    private static Box BuildStsz(List<Sample> samples)
    {
        var data = new MemoryStream();
        data.Write(BeInt32(0));                 // version + flags
        data.Write(BeInt32(0));                 // sample_size 0 = sizes follow
        data.Write(BeInt32(samples.Count));
        foreach (var s in samples) data.Write(BeInt32(s.Size));
        return new Box { Type = "stsz", Data = data.ToArray() };
    }

    /// <summary>One chunk holding every sample. Legal, and it keeps stco to a
    /// single entry — which is what makes the offset patch safe.</summary>
    private static Box BuildStsc(int sampleCount)
    {
        var data = new MemoryStream();
        data.Write(BeInt32(0));
        data.Write(BeInt32(1));                 // one entry
        data.Write(BeInt32(1));                 // first_chunk
        data.Write(BeInt32(sampleCount));       // samples_per_chunk
        data.Write(BeInt32(1));                 // sample_description_index
        return new Box { Type = "stsc", Data = data.ToArray() };
    }

    /// <summary>
    /// <c>M4A </c> as the major brand, with <c>mp42</c> and <c>isom</c> listed
    /// as compatible — the combination a decoder from the car-stereo era
    /// expects on an audio-only MP4. The trailing space in <c>M4A </c> is part
    /// of the brand, not a typo: brands are four characters.
    /// </summary>
    private static byte[] BuildAudioFtyp()
    {
        var data = new MemoryStream();
        data.Write("M4A "u8);
        data.Write(BeInt32(0));          // minor version
        data.Write("M4A "u8);
        data.Write("mp42"u8);
        data.Write("isom"u8);
        return data.ToArray();
    }

    private static Box BuildStco(int offset)
    {
        var data = new MemoryStream();
        data.Write(BeInt32(0));
        data.Write(BeInt32(1));
        data.Write(BeInt32(offset));
        return new Box { Type = "stco", Data = data.ToArray() };
    }

    // ── Header patching ───────────────────────────────────────────────────

    private static uint PatchMdhd(Box mdhd, long duration)
    {
        var d = mdhd.Data;
        if (d.Length < 4) return 0;

        if (d[0] == 1)                                   // 64-bit times
        {
            if (d.Length < 32) return 0;
            var ts = BeUInt32(d, 20);
            WriteBeUInt64(d, 24, (ulong)duration);
            return ts;
        }

        if (d.Length < 20) return 0;
        var timescale = BeUInt32(d, 12);
        WriteBeUInt32(d, 16, (uint)Math.Min(duration, uint.MaxValue));
        return timescale;
    }

    private static uint ReadTimescale(Box mvhd)
    {
        var d = mvhd.Data;
        if (d.Length < 4) return 0;
        return d[0] == 1
            ? (d.Length >= 28 ? BeUInt32(d, 20) : 0)
            : (d.Length >= 16 ? BeUInt32(d, 12) : 0);
    }

    private static void PatchDuration(Box box, long duration, bool isMvhd)
    {
        var d = box.Data;
        if (d.Length < 4) return;

        // mvhd:  [ver+flags][create][modify][timescale][duration]
        // tkhd:  [ver+flags][create][modify][track_id][reserved][duration]
        var offset32 = isMvhd ? 16 : 20;
        var offset64 = isMvhd ? 24 : 28;

        if (d[0] == 1)
        {
            if (d.Length >= offset64 + 8) WriteBeUInt64(d, offset64, (ulong)duration);
        }
        else if (d.Length >= offset32 + 4)
        {
            WriteBeUInt32(d, offset32, (uint)Math.Min(duration, uint.MaxValue));
        }
    }

    // ── Box parsing ───────────────────────────────────────────────────────

    private static List<Box> ParseBoxes(byte[] d, int start, int end)
    {
        var boxes = new List<Box>();
        var pos = start;

        while (pos + 8 <= end)
        {
            long size = BeUInt32(d, pos);
            var type = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);
            var headerLen = 8;

            if (size == 1)
            {
                if (pos + 16 > end) break;
                size = (long)BeUInt64(d, pos + 8);
                headerLen = 16;
            }
            else if (size == 0)
            {
                size = end - pos;                    // "to end of file"
            }

            if (size < headerLen || pos + size > end) break;

            var box = new Box { Type = type, SourceOffset = pos };
            var contentStart = pos + headerLen;
            var contentEnd = (int)(pos + size);

            if (box.IsContainer) box.Children.AddRange(ParseBoxes(d, contentStart, contentEnd));
            else box.Data = d[contentStart..contentEnd];

            boxes.Add(box);
            pos = contentEnd;
        }

        return boxes;
    }

    // ── Big-endian helpers. MP4 is big-endian throughout. ─────────────────

    private static byte[] BeInt32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static uint BeUInt32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    private static ulong BeUInt64(byte[] d, int o) =>
        ((ulong)BeUInt32(d, o) << 32) | BeUInt32(d, o + 4);

    private static void WriteBeUInt32(byte[] d, int o, uint v)
    {
        d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
    }

    private static void WriteBeUInt64(byte[] d, int o, ulong v)
    {
        WriteBeUInt32(d, o, (uint)(v >> 32));
        WriteBeUInt32(d, o + 4, (uint)v);
    }
}
