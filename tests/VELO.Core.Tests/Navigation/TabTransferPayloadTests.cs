using System.Text.Json;
using VELO.Core.Navigation;
using Xunit;

namespace VELO.Core.Tests.Navigation;

/// <summary>
/// v2.4.52 — coverage for the cross-window tab transfer payload. The wire
/// contract is JSON serialised into a WPF DataObject; these tests pin the
/// round-trip + the format identifier the OLE drag-drop layer keys on.
/// Cross-window WPF drag-drop itself is impossible to unit-test (needs an
/// STA dispatcher + a second process); the integration is verified by hand
/// per the maintainer's runtime gate.
/// </summary>
public class TabTransferPayloadTests
{
    [Fact]
    public void DataFormat_isStable_VELOPrefixed()
    {
        // Picking a VELO-prefixed identifier means dragged tabs never
        // accidentally land in third-party apps (a generic 'Text' fallback
        // would let Notepad receive the tab id, for example).
        Assert.Equal("VELO.Tab.Transfer", TabTransferPayload.DataFormat);
        Assert.StartsWith("VELO.", TabTransferPayload.DataFormat);
    }

    [Fact]
    public void JsonRoundTrip_preservesAllFields()
    {
        var snap = new TabSnapshot(ScrollX: 120, ScrollY: 800);
        var p    = new TabTransferPayload(
            SourceSidebarId: "abc123",
            TabId:           "tab-7",
            Url:             "https://example.com/page",
            Title:           "Example Domain",
            ContainerId:     "work",
            Snapshot:        snap);

        var json = JsonSerializer.Serialize(p);
        var back = JsonSerializer.Deserialize<TabTransferPayload>(json);

        Assert.NotNull(back);
        Assert.Equal("abc123",                  back!.SourceSidebarId);
        Assert.Equal("tab-7",                   back.TabId);
        Assert.Equal("https://example.com/page", back.Url);
        Assert.Equal("Example Domain",          back.Title);
        Assert.Equal("work",                    back.ContainerId);
        Assert.NotNull(back.Snapshot);
        Assert.Equal(120, back.Snapshot!.ScrollX);
        Assert.Equal(800, back.Snapshot.ScrollY);
    }

    [Fact]
    public void JsonRoundTrip_handlesNullSnapshot()
    {
        // The source captures the snapshot best-effort — when it fails (page
        // not loaded yet, ExecuteScriptAsync throws) the payload travels
        // with null. The receiver tolerates this and just lands at the top
        // of the page.
        var p = new TabTransferPayload(
            SourceSidebarId: "xyz",
            TabId:           "t1",
            Url:             "https://x",
            Title:           "",
            ContainerId:     "none",
            Snapshot:        null);

        var json = JsonSerializer.Serialize(p);
        var back = JsonSerializer.Deserialize<TabTransferPayload>(json);

        Assert.NotNull(back);
        Assert.Null(back!.Snapshot);
    }

    [Fact]
    public void RecordEquality_isValueBased()
    {
        var a = new TabTransferPayload("sb1", "t1", "https://x", "T", "personal", new TabSnapshot(10, 20));
        var b = new TabTransferPayload("sb1", "t1", "https://x", "T", "personal", new TabSnapshot(10, 20));
        var c = new TabTransferPayload("sb1", "t1", "https://x", "T", "personal", new TabSnapshot(10, 21));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void DifferentSourceSidebarIds_areUnequal()
    {
        // The SourceSidebarId differentiates local-reorder drops (rejected
        // by Sidebar_Drop) from cross-window transfers (accepted). Pin the
        // equality semantics so a future field reordering can't accidentally
        // collapse the discriminator.
        var sameTab = new TabTransferPayload("sidebarA", "t", "https://x", "T", "none", null);
        var otherWindow = new TabTransferPayload("sidebarB", "t", "https://x", "T", "none", null);

        Assert.NotEqual(sameTab, otherWindow);
    }

    [Fact]
    public void Deserialize_handlesMalformedJson_gracefully()
    {
        // JsonSerializer throws on malformed input — host code is wrapped in
        // try/catch to fail-soft. Pin the behaviour: malformed JSON DOES
        // throw at the serializer layer.
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<TabTransferPayload>("not-json"));
    }
}
