using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VELO.Security.Sentinel;

/// <summary>
/// S-C — Minimal WordPiece tokenizer that reads a HuggingFace
/// <c>tokenizer.json</c> (the exact file published as an asset of the
/// <c>model-v1</c> release) and reproduces the encoding
/// <c>training/sentinel/evaluate.py</c> uses, so the C# runtime and the
/// Python gate agree token-for-token.
///
/// Why hand-rolled instead of a NuGet tokenizer: the model was exported with
/// a plain BERT-uncased pipeline (BertNormalizer → BertPreTokenizer →
/// WordPiece → <c>[CLS] … [SEP]</c>) and the inputs are hosts — short, ASCII
/// in practice, never a sentence pair. That is ~150 lines here versus another
/// dependency that has to stay version-pinned alongside ONNX Runtime.
///
/// Only the pieces the published tokenizer.json actually declares are
/// implemented. <see cref="FromJson"/> throws when it sees a configuration it
/// would silently mis-encode (a non-WordPiece model, a non-Bert normalizer),
/// because a tokenizer that quietly disagrees with training produces confident
/// nonsense — worse than no model at all (lesson #33).
/// </summary>
public sealed class WordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly string _continuingPrefix;
    private readonly int _maxCharsPerWord;
    private readonly bool _lowercase;
    private readonly bool _stripAccents;
    private readonly bool _handleChineseChars;
    private readonly bool _cleanText;

    public int UnkId { get; }
    public int ClsId { get; }
    public int SepId { get; }
    public int PadId { get; }
    public int VocabSize => _vocab.Count;

    private WordPieceTokenizer(
        Dictionary<string, int> vocab,
        string continuingPrefix,
        int maxCharsPerWord,
        bool lowercase,
        bool stripAccents,
        bool handleChineseChars,
        bool cleanText,
        int unkId, int clsId, int sepId, int padId)
    {
        _vocab              = vocab;
        _continuingPrefix   = continuingPrefix;
        _maxCharsPerWord    = maxCharsPerWord;
        _lowercase          = lowercase;
        _stripAccents       = stripAccents;
        _handleChineseChars = handleChineseChars;
        _cleanText          = cleanText;
        UnkId = unkId; ClsId = clsId; SepId = sepId; PadId = padId;
    }

    public static WordPieceTokenizer FromFile(string path)
        => FromJson(File.ReadAllText(path));

    public static WordPieceTokenizer FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("model", out var model))
            throw new InvalidDataException("tokenizer.json has no \"model\" section");

        var modelType = model.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (!string.Equals(modelType, "WordPiece", StringComparison.Ordinal))
            throw new InvalidDataException($"unsupported tokenizer model \"{modelType}\" (expected WordPiece)");

        var unkToken = model.TryGetProperty("unk_token", out var u) ? u.GetString() ?? "[UNK]" : "[UNK]";
        var prefix   = model.TryGetProperty("continuing_subword_prefix", out var p) ? p.GetString() ?? "##" : "##";
        var maxChars = model.TryGetProperty("max_input_chars_per_word", out var m) ? m.GetInt32() : 100;

        if (!model.TryGetProperty("vocab", out var vocabEl) || vocabEl.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("tokenizer.json has no \"model.vocab\" object");

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in vocabEl.EnumerateObject())
            vocab[entry.Name] = entry.Value.GetInt32();

        // Normalizer. strip_accents defaults to the value of lowercase when the
        // field is null — same rule as the rust `BertNormalizer`.
        bool lowercase = true, handleChinese = true, cleanText = true;
        bool? stripAccents = null;
        if (root.TryGetProperty("normalizer", out var norm) && norm.ValueKind == JsonValueKind.Object)
        {
            var normType = norm.TryGetProperty("type", out var nt) ? nt.GetString() : null;
            if (!string.Equals(normType, "BertNormalizer", StringComparison.Ordinal))
                throw new InvalidDataException($"unsupported normalizer \"{normType}\" (expected BertNormalizer)");

            if (norm.TryGetProperty("lowercase", out var lc) && lc.ValueKind is JsonValueKind.True or JsonValueKind.False)
                lowercase = lc.GetBoolean();
            if (norm.TryGetProperty("clean_text", out var ct) && ct.ValueKind is JsonValueKind.True or JsonValueKind.False)
                cleanText = ct.GetBoolean();
            if (norm.TryGetProperty("handle_chinese_chars", out var hc) && hc.ValueKind is JsonValueKind.True or JsonValueKind.False)
                handleChinese = hc.GetBoolean();
            if (norm.TryGetProperty("strip_accents", out var sa) && sa.ValueKind is JsonValueKind.True or JsonValueKind.False)
                stripAccents = sa.GetBoolean();
        }

        int Lookup(string token, int fallback)
            => vocab.TryGetValue(token, out var id) ? id : fallback;

        var unkId = Lookup(unkToken, 100);
        return new WordPieceTokenizer(
            vocab, prefix, maxChars,
            lowercase, stripAccents ?? lowercase, handleChinese, cleanText,
            unkId,
            Lookup("[CLS]", 101),
            Lookup("[SEP]", 102),
            Lookup("[PAD]", 0));
    }

    /// <summary>
    /// Encodes <paramref name="text"/> to a fixed-width
    /// <c>[CLS] … [SEP]</c> sequence of <paramref name="maxLen"/> tokens,
    /// right-padded with <c>[PAD]</c>. Returns the parallel attention mask
    /// (1 = real token, 0 = padding). Matches the Python side's
    /// <c>truncation=True, max_length=MAX_LEN, padding="max_length"</c>.
    /// </summary>
    public (long[] InputIds, long[] AttentionMask) Encode(string text, int maxLen)
    {
        if (maxLen < 2) throw new ArgumentOutOfRangeException(nameof(maxLen), "need room for [CLS] and [SEP]");

        var ids  = new long[maxLen];
        var mask = new long[maxLen];

        var pieces = TokenizeToIds(text ?? "", maxLen - 2);

        var n = 0;
        ids[n++] = ClsId;
        foreach (var id in pieces) ids[n++] = id;
        ids[n++] = SepId;

        for (var i = 0; i < n; i++) mask[i] = 1;
        for (var i = n; i < maxLen; i++) ids[i] = PadId;

        return (ids, mask);
    }

    /// <summary>Exposed for tests — the token strings before ids are looked up.</summary>
    public IReadOnlyList<string> Tokenize(string text)
    {
        var result = new List<string>();
        foreach (var word in PreTokenize(Normalize(text ?? "")))
            result.AddRange(WordPieces(word));
        return result;
    }

    private List<long> TokenizeToIds(string text, int limit)
    {
        var ids = new List<long>(limit);
        foreach (var word in PreTokenize(Normalize(text)))
        {
            foreach (var piece in WordPieces(word))
            {
                if (ids.Count >= limit) return ids;
                ids.Add(_vocab.TryGetValue(piece, out var id) ? id : UnkId);
            }
        }
        return ids;
    }

    // ── Normalizer ────────────────────────────────────────────────────────

    internal string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length + 8);

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;

            if (_cleanText)
            {
                // Drop NUL / replacement char / control chars; fold the rest of
                // whitespace to a plain space so the pre-tokenizer sees one rule.
                if (value == 0 || value == 0xFFFD) continue;
                if (Rune.IsControl(rune) && value is not ('\t' or '\n' or '\r')) continue;
                if (value is '\t' or '\n' or '\r' || Rune.IsWhiteSpace(rune)) { sb.Append(' '); continue; }
            }

            if (_handleChineseChars && IsChineseChar(value))
            {
                sb.Append(' ').Append(rune.ToString()).Append(' ');
                continue;
            }

            sb.Append(rune.ToString());
        }

        var s = sb.ToString();

        if (_stripAccents)
        {
            var decomposed = s.Normalize(NormalizationForm.FormD);
            var stripped = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    stripped.Append(c);
            }
            s = stripped.ToString();
        }

        return _lowercase ? s.ToLowerInvariant() : s;
    }

    private static bool IsChineseChar(int cp) =>
        (cp >= 0x4E00  && cp <= 0x9FFF)  ||
        (cp >= 0x3400  && cp <= 0x4DBF)  ||
        (cp >= 0x20000 && cp <= 0x2A6DF) ||
        (cp >= 0x2A700 && cp <= 0x2B73F) ||
        (cp >= 0x2B740 && cp <= 0x2B81F) ||
        (cp >= 0x2B820 && cp <= 0x2CEAF) ||
        (cp >= 0xF900  && cp <= 0xFAFF)  ||
        (cp >= 0x2F800 && cp <= 0x2FA1F);

    // ── Pre-tokenizer (BertPreTokenizer) ──────────────────────────────────
    //
    // Split on whitespace (dropped), then isolate every punctuation char as
    // its own token. For a host that means paypal-secure.xyz →
    // [paypal] [-] [secure] [.] [xyz].

    internal static IEnumerable<string> PreTokenize(string normalized)
    {
        var current = new StringBuilder();

        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }

            if (IsBertPunctuation(c))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                yield return c.ToString();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) yield return current.ToString();
    }

    private static bool IsBertPunctuation(char c)
    {
        // BERT treats every non-alphanumeric ASCII char as punctuation, plus
        // anything Unicode categorises as punctuation.
        if (c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~')
            return true;

        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.DashPunctuation or
            UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation or
            UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };
    }

    // ── WordPiece (greedy longest-match-first) ────────────────────────────

    private IEnumerable<string> WordPieces(string word)
    {
        if (word.Length > _maxCharsPerWord)
        {
            yield return "[UNK]";
            yield break;
        }

        var pieces = new List<string>();
        var start = 0;

        while (start < word.Length)
        {
            var end = word.Length;
            string? match = null;

            while (start < end)
            {
                var candidate = start == 0
                    ? word[start..end]
                    : _continuingPrefix + word[start..end];

                if (_vocab.ContainsKey(candidate)) { match = candidate; break; }
                end--;
            }

            if (match is null)
            {
                // Unknown at some offset ⇒ the whole word is [UNK], same as
                // the reference implementation (not a partial encoding).
                yield return "[UNK]";
                yield break;
            }

            pieces.Add(match);
            start = end;
        }

        foreach (var piece in pieces) yield return piece;
    }
}
