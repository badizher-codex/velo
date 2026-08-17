using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / D-1 — turning the fragmented MP4 the capture receives into the
/// plain one a fixed-function decoder can read.
///
/// Verified in the field first: a real capture went from "listed but silent"
/// to playing in a 2014 car stereo. These pin the behaviour that made that
/// work, on inputs built here rather than on a file that only exists on one
/// machine.
/// </summary>
public class Mp4FlattenerTests
{
    [Fact]
    public void A_fragmented_file_comes_out_plain()
    {
        var input = Fmp4([[1, 2, 3, 4], [5, 6, 7, 8, 9], [10, 11]]);

        using var ms = new MemoryStream();
        var result = Mp4Flattener.ToPlainMp4(input, ms);

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.Packets);

        var boxes = TopLevel(ms.ToArray());
        Assert.DoesNotContain("moof", boxes);          // nothing left fragmented
        Assert.Contains("moov", boxes);
        Assert.Contains("mdat", boxes);
    }

    [Fact]
    public void The_index_the_decoder_needs_is_present_and_mvex_is_gone()
    {
        // The exact failure this exists for: VLC reported "read 0 samples" on
        // the capture because these tables were empty and mvex said "look in
        // the fragments" — fragments a car stereo cannot follow.
        var output = Flatten([[1, 2, 3], [4, 5, 6]]);
        var text = System.Text.Encoding.ASCII.GetString(output);

        Assert.Contains("stts", text);
        Assert.Contains("stsz", text);
        Assert.Contains("stsc", text);
        Assert.Contains("stco", text);
        Assert.DoesNotContain("mvex", text);
    }

    [Fact]
    public void Moov_is_written_before_mdat()
    {
        // Researched, not assumed: hardware decoders often cannot seek back
        // for an index at the end, so a trailing moov is a common reason a
        // valid MP4 will not play on exactly the devices this targets.
        var output = Flatten([[1, 2, 3, 4]]);
        var text = System.Text.Encoding.ASCII.GetString(output);

        Assert.True(text.IndexOf("moov", StringComparison.Ordinal)
                  < text.IndexOf("mdat", StringComparison.Ordinal),
            "moov must precede mdat");
    }

    [Fact]
    public void Every_sample_byte_survives_in_order()
    {
        // The claim the whole feature rests on: this is a rewrap, so the audio
        // that goes in is the audio that comes out.
        byte[][] samples = [[0xAA, 0xBB], [0xCC], [0xDD, 0xEE, 0xFF]];
        var output = Flatten(samples);

        var expected = samples.SelectMany(s => s).ToArray();
        var mdat = MdatPayload(output);

        Assert.Equal(expected, mdat);
    }

    [Fact]
    public void Sample_sizes_are_recorded_individually()
    {
        var output = Flatten([[1, 2], [3, 4, 5, 6], [7]]);
        var stsz = FindBoxPayload(output, "stsz");

        Assert.Equal(0, BeInt(stsz, 4));      // sample_size 0 = a table follows
        Assert.Equal(3, BeInt(stsz, 8));      // three samples
        Assert.Equal(2, BeInt(stsz, 12));
        Assert.Equal(4, BeInt(stsz, 16));
        Assert.Equal(1, BeInt(stsz, 20));
    }

    [Fact]
    public void Equal_durations_collapse_into_one_stts_run()
    {
        var output = Flatten([[1], [2], [3], [4]]);
        var stts = FindBoxPayload(output, "stts");

        Assert.Equal(1, BeInt(stts, 4));       // one run…
        Assert.Equal(4, BeInt(stts, 8));       // …covering four samples
        Assert.Equal(1024, BeInt(stts, 12));   // at the fragment's duration
    }

    [Fact]
    public void The_dash_brand_is_replaced_with_an_audio_one()
    {
        // The capture arrives branded `dash`, which announces a fragmented
        // streaming file. Leaving that on a file that is no longer fragmented
        // invites a strict decoder to look for fragments and give up.
        var output = Flatten([[1, 2, 3]]);
        var brand = System.Text.Encoding.ASCII.GetString(output, 8, 4);

        Assert.Equal("M4A ", brand);
    }

    [Fact]
    public void The_media_duration_is_filled_in()
    {
        // The init segment ships duration zero — the real length only exists
        // once the fragments are counted. A file claiming zero length is one a
        // player shows as 0:00 and refuses to seek in.
        var output = Flatten([[1], [2], [3]]);
        var mdhd = FindBoxPayload(output, "mdhd");

        Assert.Equal(3 * 1024, BeInt(mdhd, 16));
    }

    // ── Refusals ─────────────────────────────────────────────────────────

    [Fact]
    public void Something_that_is_not_an_mp4_is_refused()
    {
        var result = Mp4Flattener.ToPlainMp4("still not a media file"u8.ToArray(), new MemoryStream());

        Assert.False(result.Success);
        Assert.Contains("moov", result.Error);
    }

    [Fact]
    public void A_file_with_no_fragments_says_so_rather_than_writing_nothing()
    {
        // An already-plain MP4 has no moof to read, so flattening it would
        // produce a file with an empty mdat. Saying so beats emitting silence.
        var input = Fmp4([[1, 2, 3]], includeFragments: false);

        var result = Mp4Flattener.ToPlainMp4(input, new MemoryStream());

        Assert.False(result.Success);
        Assert.Contains("No fragments", result.Error);
    }

    [Fact]
    public void A_fragment_pointing_past_the_end_is_refused()
    {
        // What a capture cut off mid-write looks like. Copying from beyond the
        // buffer would either throw or splice in garbage.
        var input = Fmp4([[1, 2, 3, 4]]);
        var truncated = input[..(input.Length - 2)];

        var result = Mp4Flattener.ToPlainMp4(truncated, new MemoryStream());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static byte[] Flatten(byte[][] samples)
    {
        using var ms = new MemoryStream();
        var result = Mp4Flattener.ToPlainMp4(Fmp4(samples), ms);
        Assert.True(result.Success, result.Error);
        return ms.ToArray();
    }

    private const uint SampleDuration = 1024;   // one AAC frame

    /// <summary>
    /// The smallest fragmented MP4 that exercises the reader: ftyp branded
    /// dash, a moov whose sample tables are empty and which carries mvex, then
    /// one moof/mdat pair per call.
    /// </summary>
    private static byte[] Fmp4(byte[][] samples, bool includeFragments = true)
    {
        var stbl = Box("stbl",
            Box("stsd", [0, 0, 0, 0, 0, 0, 0, 1]),      // one (opaque) entry
            Box("stts", [0, 0, 0, 0, 0, 0, 0, 0]),
            Box("stsc", [0, 0, 0, 0, 0, 0, 0, 0]),
            Box("stsz", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
            Box("stco", [0, 0, 0, 0, 0, 0, 0, 0]));

        var mdhd = new byte[24];
        Be(mdhd, 12, 48000);                            // timescale
        Be(mdhd, 16, 0);                                // duration, filled later

        var mvhd = new byte[100];
        Be(mvhd, 12, 1000);                             // movie timescale

        var trak = Box("trak",
            Box("tkhd", new byte[84]),
            Box("mdia", Box("mdhd", mdhd), Box("minf", stbl)));

        var moov = includeFragments
            ? Box("moov", Box("mvhd", mvhd), trak, Box("mvex", Box("trex", new byte[24])))
            : Box("moov", Box("mvhd", mvhd), trak);

        var ftyp = Box("ftyp", "dash"u8.ToArray().Concat(new byte[4]).ToArray());

        if (!includeFragments) return Concat(ftyp, moov);

        // trun carries per-sample duration and size (flags 0x000300) and a
        // data offset (0x000001) measured from the start of the moof.
        var trun = new List<byte> { 0, 0, 0x03, 0x01 };
        trun.AddRange(BeBytes(samples.Length));
        var trunDataOffsetIndex = trun.Count;
        trun.AddRange(BeBytes(0));                      // patched once sizes are known
        foreach (var s in samples)
        {
            trun.AddRange(BeBytes((int)SampleDuration));
            trun.AddRange(BeBytes(s.Length));
        }

        var tfhd = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };   // no optional fields, track 1

        byte[] BuildMoof(byte[] trunPayload) => Box("moof",
            Box("mfhd", new byte[8]),
            Box("traf", Box("tfhd", tfhd), Box("trun", trunPayload)));

        // The sample data offset is measured from the start of the moof, and
        // it depends on the moof's own size — so build it once to learn the
        // size, then rebuild with the offset filled in. The size does not
        // change, because only the value of an existing field is written.
        var trunArray = trun.ToArray();
        var dataOffset = BuildMoof(trunArray).Length + 8;   // + the mdat header
        Be(trunArray, trunDataOffsetIndex, dataOffset);

        var payload = samples.SelectMany(s => s).ToArray();
        return Concat(ftyp, moov, BuildMoof(trunArray), Box("mdat", payload));
    }

    private static byte[] Box(string type, params byte[][] children)
        => Box(type, Concat(children));

    private static byte[] Box(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        return Concat(BeBytes(size), System.Text.Encoding.ASCII.GetBytes(type), payload);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (var p in parts) all.AddRange(p);
        return all.ToArray();
    }

    private static byte[] BeBytes(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static void Be(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
    }

    private static int BeInt(byte[] d, int o)
        => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    private static List<string> TopLevel(byte[] d)
    {
        var types = new List<string>();
        var pos = 0;
        while (pos + 8 <= d.Length)
        {
            var size = BeInt(d, pos);
            if (size < 8 || pos + size > d.Length) break;
            types.Add(System.Text.Encoding.ASCII.GetString(d, pos + 4, 4));
            pos += size;
        }
        return types;
    }

    private static byte[] MdatPayload(byte[] d)
    {
        var pos = 0;
        while (pos + 8 <= d.Length)
        {
            var size = BeInt(d, pos);
            var type = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);
            if (type == "mdat") return d[(pos + 8)..(pos + size)];
            if (size < 8) break;
            pos += size;
        }
        return [];
    }

    /// <summary>Finds a box anywhere in the tree by scanning for its type tag,
    /// which is enough for assertions and keeps the test independent of the
    /// flattener's own parser.</summary>
    private static byte[] FindBoxPayload(byte[] d, string type)
    {
        var tag = System.Text.Encoding.ASCII.GetBytes(type);
        for (var i = 4; i < d.Length - 8; i++)
        {
            if (d[i] != tag[0] || d[i + 1] != tag[1] || d[i + 2] != tag[2] || d[i + 3] != tag[3]) continue;
            var size = BeInt(d, i - 4);
            if (size < 8 || i - 4 + size > d.Length) continue;
            return d[(i + 4)..(i - 4 + size)];
        }
        Assert.Fail($"box '{type}' not found");
        return [];
    }
}
