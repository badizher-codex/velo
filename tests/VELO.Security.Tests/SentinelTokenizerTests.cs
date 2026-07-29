using VELO.Security.Sentinel;
using Xunit;

namespace VELO.Security.Tests;

/// <summary>
/// S-C — parity between the C# <see cref="WordPieceTokenizer"/> and the
/// HuggingFace tokenizer the model was trained and gated with.
///
/// The golden vectors below were produced by
/// <c>training/sentinel/.venv</c>'s transformers against
/// <c>out/model</c> with exactly the call the Python side uses:
/// <c>tok(host, truncation=True, max_length=32, padding="max_length")</c>.
/// They are the contract: if the C# encoding drifts, the model receives
/// different tokens than the ones AUC 0.9907 / FPR 0.74% were measured on,
/// and every gate from S-B becomes meaningless.
/// </summary>
public class SentinelTokenizerTests
{
    private const int MaxLen = 32;

    private static WordPieceTokenizer Load()
        => WordPieceTokenizer.FromFile(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer.json"));

    public static TheoryData<string, int[]> GoldenEncodings => new()
    {
        { "github.com",                new[] { 101, 21025, 2705, 12083, 1012, 4012, 102 } },
        { "www.paypal-secure.xyz",     new[] { 101, 7479, 1012, 3477, 12952, 1011, 5851, 1012, 1060, 2100, 2480, 102 } },
        { "login.microsoftonline.com", new[] { 101, 8833, 2378, 1012, 7513, 2239, 4179, 1012, 4012, 102 } },
        { "doubleclick.net",           new[] { 101, 3313, 20464, 6799, 1012, 5658, 102 } },
        { "xn--80ak6aa92e.com",        new[] { 101, 1060, 2078, 1011, 1011, 3770, 4817, 2575, 11057, 2683, 2475, 2063, 1012, 4012, 102 } },
        { "a1b2c3d4e5.top",            new[] { 101, 17350, 2497, 2475, 2278, 29097, 2549, 2063, 2629, 1012, 2327, 102 } },
        { "bbc.co.uk",                 new[] { 101, 4035, 1012, 2522, 1012, 2866, 102 } },
        { "пример.рф",                 new[] { 101, 1194, 16856, 10325, 29745, 15290, 16856, 1012, 1195, 29749, 102 } },
        { "mail.google.com",           new[] { 101, 5653, 1012, 8224, 1012, 4012, 102 } },
        { "cdn.jsdelivr.net",          new[] { 101, 3729, 2078, 1012, 1046, 16150, 20806, 19716, 1012, 5658, 102 } },
    };

    [Theory]
    [MemberData(nameof(GoldenEncodings))]
    public void Encode_matches_the_python_tokenizer(string host, int[] expectedPrefix)
    {
        var (ids, mask) = Load().Encode(host, MaxLen);

        Assert.Equal(MaxLen, ids.Length);
        Assert.Equal(MaxLen, mask.Length);

        for (var i = 0; i < expectedPrefix.Length; i++)
            Assert.Equal(expectedPrefix[i], ids[i]);

        // Everything past [SEP] is [PAD] with mask 0.
        for (var i = expectedPrefix.Length; i < MaxLen; i++)
        {
            Assert.Equal(0, ids[i]);
            Assert.Equal(0, mask[i]);
        }

        for (var i = 0; i < expectedPrefix.Length; i++)
            Assert.Equal(1, mask[i]);
    }

    [Fact]
    public void Tokenize_splits_punctuation_the_bert_way()
    {
        // paypal-secure.xyz → the '-' and '.' are their own tokens, and the
        // sub-words carry the ## continuation prefix.
        Assert.Equal(
            new[] { "www", ".", "pay", "##pal", "-", "secure", ".", "x", "##y", "##z" },
            Load().Tokenize("www.paypal-secure.xyz"));
    }

    [Fact]
    public void Tokenize_lowercases_and_strips_accents()
    {
        // BertNormalizer with lowercase=true and strip_accents=null strips
        // accents too. A host that only differs by case must encode identically.
        var tok = Load();
        Assert.Equal(tok.Tokenize("github.com"), tok.Tokenize("GitHub.COM"));
        Assert.Equal(tok.Tokenize("banco.com"),  tok.Tokenize("bánco.com"));
    }

    [Fact]
    public void Encode_truncates_long_hosts_and_keeps_the_separators()
    {
        var absurd = string.Join(".", Enumerable.Repeat("subdomain", 30)) + ".com";
        var (ids, mask) = Load().Encode(absurd, MaxLen);

        Assert.Equal(MaxLen, ids.Length);
        Assert.Equal(101, ids[0]);                // [CLS]
        Assert.Equal(102, ids[MaxLen - 1]);       // [SEP] survives truncation
        Assert.All(mask, m => Assert.Equal(1, m));
    }

    [Fact]
    public void Encode_of_empty_input_is_just_cls_sep()
    {
        var (ids, mask) = Load().Encode("", MaxLen);
        Assert.Equal(101, ids[0]);
        Assert.Equal(102, ids[1]);
        Assert.Equal(0,   ids[2]);
        Assert.Equal(1,   mask[0]);
        Assert.Equal(1,   mask[1]);
        Assert.Equal(0,   mask[2]);
    }

    [Fact]
    public void FromJson_refuses_a_tokenizer_it_would_misencode()
    {
        // Lesson #33 — a fallback that plausibly mis-encodes is worse than no
        // model. A BPE tokenizer parsed as WordPiece would produce confident
        // nonsense, so the load must fail loudly and let the caller fail soft.
        var bpe = """{"model":{"type":"BPE","vocab":{},"merges":[]}}""";
        Assert.Throws<InvalidDataException>(() => WordPieceTokenizer.FromJson(bpe));

        var strangeNormalizer =
            """{"normalizer":{"type":"Precompiled"},"model":{"type":"WordPiece","vocab":{"a":1}}}""";
        Assert.Throws<InvalidDataException>(() => WordPieceTokenizer.FromJson(strangeNormalizer));
    }

    [Fact]
    public void Special_token_ids_come_from_the_vocab()
    {
        var tok = Load();
        Assert.Equal(0,   tok.PadId);
        Assert.Equal(100, tok.UnkId);
        Assert.Equal(101, tok.ClsId);
        Assert.Equal(102, tok.SepId);
        Assert.True(tok.VocabSize > 30_000);
    }
}
