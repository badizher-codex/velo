using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 / D-1 — rewrapping Opus from WebM into Ogg without a transcoder.
///
/// The inputs here are built by hand rather than loaded from disk: a test that
/// needs a 3 MB capture next to it only runs on one machine, and the point of
/// putting the remuxer in Core was that it is a pure byte-to-byte function.
/// The real capture was verified separately, by handing the output to VLC.
/// </summary>
public class OggOpusRemuxerTests
{
    // ── Opus packet duration (RFC 6716 §3.1) ─────────────────────────────

    [Theory]
    // config in the top 5 bits, frame-count code in the bottom 2.
    [InlineData(0x00, 480)]    // config 0  — SILK  10 ms, one frame
    [InlineData(0x08, 960)]    // config 1  — SILK  20 ms
    [InlineData(0x10, 1920)]   // config 2  — SILK  40 ms
    [InlineData(0x18, 2880)]   // config 3  — SILK  60 ms
    [InlineData(0x60, 480)]    // config 12 — hybrid 10 ms
    [InlineData(0x68, 960)]    // config 13 — hybrid 20 ms
    [InlineData(0x80, 120)]    // config 16 — CELT 2.5 ms
    [InlineData(0x88, 240)]    // config 17 — CELT 5 ms
    [InlineData(0x98, 960)]    // config 19 — CELT 20 ms
    public void Packet_duration_comes_from_the_toc_byte(byte toc, int expected)
    {
        Assert.Equal(expected, OggOpusRemuxer.PacketSamples([toc, 0x00]));
    }

    [Fact]
    public void Two_frame_packets_count_double()
    {
        // Code 1 and 2 both mean two frames. Getting this wrong makes granule
        // positions drift and the reported duration comes out half-length.
        Assert.Equal(1920, OggOpusRemuxer.PacketSamples([0x08 | 1, 0x00]));
        Assert.Equal(1920, OggOpusRemuxer.PacketSamples([0x08 | 2, 0x00]));
    }

    [Fact]
    public void Arbitrary_frame_count_reads_the_second_byte()
    {
        // Code 3: the frame count lives in the low 6 bits of the next byte.
        Assert.Equal(960 * 3, OggOpusRemuxer.PacketSamples([0x08 | 3, 3]));
    }

    [Fact]
    public void An_empty_packet_has_no_duration()
    {
        Assert.Equal(0, OggOpusRemuxer.PacketSamples([]));
    }

    // ── The remux ────────────────────────────────────────────────────────

    [Fact]
    public void A_webm_opus_track_becomes_a_valid_ogg_stream()
    {
        var webm = BuildWebm(OpusHead(channels: 2), [Packet(0x08, 40), Packet(0x08, 40)]);

        using var ms = new MemoryStream();
        var result = OggOpusRemuxer.ToOggOpus(webm, ms);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.Packets);

        var pages = ParseOggPages(ms.ToArray());

        // Ogg-Opus is defined as: OpusHead alone on the first page, OpusTags on
        // the second, audio after. A player that finds anything else in those
        // two pages rejects the file.
        Assert.True(pages.Count >= 3);
        Assert.StartsWith("OpusHead", pages[0].Payload);
        Assert.StartsWith("OpusTags", pages[1].Payload);

        Assert.Equal(0x02, pages[0].HeaderType);            // BOS on the first
        Assert.Equal(0x04, pages[^1].HeaderType);           // EOS on the last
        Assert.All(pages, p => Assert.True(p.CrcValid, "a page carries a bad CRC"));
    }

    [Fact]
    public void Granule_positions_accumulate_packet_durations()
    {
        // Two 20 ms packets at 48 kHz = 1920 samples on the final page.
        var webm = BuildWebm(OpusHead(2), [Packet(0x08, 40), Packet(0x08, 40)]);

        using var ms = new MemoryStream();
        Assert.True(OggOpusRemuxer.ToOggOpus(webm, ms).Success);

        var pages = ParseOggPages(ms.ToArray());
        Assert.Equal(0, pages[0].Granule);     // headers carry granule zero
        Assert.Equal(0, pages[1].Granule);
        Assert.Equal(1920, pages[^1].Granule);
    }

    [Fact]
    public void Page_sequence_numbers_start_at_zero_and_never_skip()
    {
        var webm = BuildWebm(OpusHead(2), [Packet(0x08, 40)]);
        using var ms = new MemoryStream();
        Assert.True(OggOpusRemuxer.ToOggOpus(webm, ms).Success);

        var pages = ParseOggPages(ms.ToArray());
        for (var i = 0; i < pages.Count; i++) Assert.Equal(i, pages[i].Sequence);
    }

    // ── Refusals: a wrong answer is worse than no answer ─────────────────

    [Fact]
    public void Something_that_is_not_matroska_is_refused_by_name()
    {
        var result = OggOpusRemuxer.ToOggOpus("not a media file at all"u8.ToArray(), new MemoryStream());

        Assert.False(result.Success);
        Assert.Contains("EBML", result.Error);
    }

    [Fact]
    public void A_matroska_without_an_opus_track_is_refused()
    {
        // Same container, a different codec. Emitting an "Opus" file built from
        // Vorbis packets would produce noise, which is the failure this guards.
        var webm = BuildWebm(OpusHead(2), [Packet(0x08, 40)], codecId: "A_VORBIS");

        var result = OggOpusRemuxer.ToOggOpus(webm, new MemoryStream());

        Assert.False(result.Success);
        Assert.Contains("Opus", result.Error);
    }

    [Fact]
    public void An_opus_track_with_no_frames_is_refused()
    {
        var webm = BuildWebm(OpusHead(2), []);

        var result = OggOpusRemuxer.ToOggOpus(webm, new MemoryStream());

        Assert.False(result.Success);
        Assert.Contains("no audio frames", result.Error);
    }

    [Fact]
    public void Xiph_lacing_is_refused_rather_than_guessed()
    {
        // Never seen from the capture, but a block that uses it would silently
        // yield mis-split packets — a file that plays as noise. Refusing names
        // the reason instead.
        var webm = BuildWebm(OpusHead(2), [Packet(0x08, 40)], lacingFlags: 0x02);

        var result = OggOpusRemuxer.ToOggOpus(webm, new MemoryStream());

        Assert.False(result.Success);
        Assert.Contains("lacing", result.Error);
    }

    [Fact]
    public void Truncated_input_fails_without_throwing()
    {
        // This runs off a UI action, so a half-written file must come back as
        // a message rather than an exception through the click handler.
        var webm = BuildWebm(OpusHead(2), [Packet(0x08, 40)]);
        var cut  = webm[..(webm.Length / 2)];

        var result = OggOpusRemuxer.ToOggOpus(cut, new MemoryStream());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── Builders ─────────────────────────────────────────────────────────

    private static byte[] OpusHead(int channels)
    {
        var h = new byte[19];
        "OpusHead"u8.CopyTo(h);
        h[8]  = 1;                                       // version
        h[9]  = (byte)channels;
        BitConverter.GetBytes((ushort)3840).CopyTo(h, 10);  // pre-skip
        BitConverter.GetBytes(48000).CopyTo(h, 12);         // input sample rate
        return h;
    }

    private static byte[] Packet(byte toc, int payloadBytes)
    {
        var p = new byte[payloadBytes + 1];
        p[0] = toc;
        for (var i = 1; i < p.Length; i++) p[i] = (byte)(i & 0xFF);
        return p;
    }

    /// <summary>
    /// The smallest Matroska that exercises the reader: EBML header, Segment,
    /// Tracks with one entry, and one Cluster of SimpleBlocks.
    /// </summary>
    private static byte[] BuildWebm(
        byte[] codecPrivate, byte[][] packets, string codecId = "A_OPUS", byte lacingFlags = 0x00)
    {
        var trackEntry = Concat(
            Element(0xD7, [0x01]),                                   // TrackNumber 1
            Element(0x83, [0x02]),                                   // TrackType audio
            Element(0x86, System.Text.Encoding.ASCII.GetBytes(codecId)),
            Element(0x63A2, codecPrivate));

        var tracks = Element(0x1654AE6B, Element(0xAE, trackEntry));

        var blocks = new List<byte>();
        foreach (var p in packets)
        {
            var body = new List<byte> { 0x81, 0x00, 0x00, lacingFlags };  // track 1, timecode, flags
            if (lacingFlags != 0x00) body.Add(0x00);                       // lacing frame count
            body.AddRange(p);
            blocks.AddRange(Element(0xA3, body.ToArray()));
        }

        var cluster = Element(0x1F43B675, Concat(Element(0xE7, [0x00]), blocks.ToArray()));
        var segment = Element(0x18538067, Concat(tracks, cluster));
        var ebml    = Element(0x1A45DFA3, Element(0x4286, [0x01]));

        return Concat(ebml, segment);
    }

    private static byte[] Element(uint id, byte[] payload)
        => Concat(IdBytes(id), SizeBytes(payload.Length), payload);

    private static byte[] IdBytes(uint id)
    {
        if (id <= 0xFF)     return [(byte)id];
        if (id <= 0xFFFF)   return [(byte)(id >> 8), (byte)id];
        if (id <= 0xFFFFFF) return [(byte)(id >> 16), (byte)(id >> 8), (byte)id];
        return [(byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id];
    }

    /// <summary>Four-byte VINT throughout — always legal, and it keeps the
    /// builder from having to reason about width.</summary>
    private static byte[] SizeBytes(int size) =>
        [(byte)(0x10 | ((size >> 24) & 0x0F)), (byte)(size >> 16), (byte)(size >> 8), (byte)size];

    private static byte[] Concat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (var p in parts) all.AddRange(p);
        return all.ToArray();
    }

    // ── An independent Ogg reader, so the tests do not trust the writer ──

    private sealed record OggPage(byte HeaderType, long Granule, int Sequence, string Payload, bool CrcValid);

    private static List<OggPage> ParseOggPages(byte[] data)
    {
        var pages = new List<OggPage>();
        var pos = 0;

        while (pos + 27 <= data.Length)
        {
            Assert.Equal("OggS", System.Text.Encoding.ASCII.GetString(data, pos, 4));

            var headerType = data[pos + 5];
            var granule    = BitConverter.ToInt64(data, pos + 6);
            var sequence   = BitConverter.ToInt32(data, pos + 18);
            var storedCrc  = BitConverter.ToUInt32(data, pos + 22);
            var segCount   = data[pos + 26];

            var headerLen = 27 + segCount;
            var bodyLen = 0;
            for (var i = 0; i < segCount; i++) bodyLen += data[pos + 27 + i];

            var page = new byte[headerLen + bodyLen];
            Array.Copy(data, pos, page, 0, page.Length);
            // The CRC is computed with its own field zeroed.
            page[22] = page[23] = page[24] = page[25] = 0;

            var payload = System.Text.Encoding.ASCII.GetString(
                data, pos + headerLen, Math.Min(bodyLen, 8));

            pages.Add(new OggPage(headerType, granule, sequence, payload, Crc(page) == storedCrc));
            pos += page.Length;
        }

        return pages;
    }

    private static uint Crc(byte[] data)
    {
        uint crc = 0;
        foreach (var b in data)
        {
            crc ^= (uint)b << 24;
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc;
    }
}
