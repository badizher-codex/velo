using VELO.Core.AI;
using Xunit;

namespace VELO.Core.Tests;

public class AiChatRouterTests
{
    private static AiChatRouter.ChatDelegate FakeChat(string label = "x")
        => (sys, user, ct) => Task.FromResult($"{label}|{sys}|{user}");

    // ── Registration ──────────────────────────────────────────────────────

    [Fact]
    public void Register_AddsToCount()
    {
        var router = new AiChatRouter();
        Assert.Equal(0, router.Count);
        router.Register(_ => { });
        Assert.Equal(1, router.Count);
        router.Register(_ => { });
        Assert.Equal(2, router.Count);
    }

    [Fact]
    public void Register_NullSetter_Throws()
    {
        var router = new AiChatRouter();
        Assert.Throws<ArgumentNullException>(() => router.Register(null!));
    }

    [Fact]
    public void Register_IsFluent()
    {
        var router = new AiChatRouter();
        var ret = router
            .Register(_ => { })
            .Register(_ => { })
            .Register(_ => { });
        Assert.Same(router, ret);
        Assert.Equal(3, router.Count);
    }

    // ── WireAll ──────────────────────────────────────────────────────────

    [Fact]
    public void WireAll_PushesDelegateToEveryConsumer()
    {
        var bagA = (AiChatRouter.ChatDelegate?)null;
        var bagB = (AiChatRouter.ChatDelegate?)null;
        var bagC = (AiChatRouter.ChatDelegate?)null;

        var router = new AiChatRouter()
            .Register(d => bagA = d)
            .Register(d => bagB = d)
            .Register(d => bagC = d);

        var chat = FakeChat();
        router.WireAll(chat);

        Assert.Same(chat, bagA);
        Assert.Same(chat, bagB);
        Assert.Same(chat, bagC);
    }

    [Fact]
    public void WireAll_NullDelegate_Throws()
    {
        var router = new AiChatRouter().Register(_ => { });
        Assert.Throws<ArgumentNullException>(() => router.WireAll(null!));
    }

    [Fact]
    public void WireAll_StoresCurrentForLaterRewire()
    {
        var router = new AiChatRouter().Register(_ => { });
        Assert.Null(router.Current);

        var chat = FakeChat();
        router.WireAll(chat);
        Assert.Same(chat, router.Current);
    }

    [Fact]
    public void WireAll_IsIdempotent_ReplacesEverywhere()
    {
        AiChatRouter.ChatDelegate? bag = null;
        var router = new AiChatRouter().Register(d => bag = d);

        var first  = FakeChat("first");
        var second = FakeChat("second");
        router.WireAll(first);
        Assert.Same(first, bag);

        router.WireAll(second);
        Assert.Same(second, bag); // replaced
        Assert.Same(second, router.Current);
    }

    [Fact]
    public void WireAll_WithoutRegistrations_StillStoresCurrent()
    {
        var router = new AiChatRouter();
        var chat = FakeChat();
        router.WireAll(chat);
        Assert.Same(chat, router.Current);
    }

    // ── Error tolerance ──────────────────────────────────────────────────

    [Fact]
    public void WireAll_FailingSetter_DoesNotAbortOthers()
    {
        AiChatRouter.ChatDelegate? bag = null;
        var router = new AiChatRouter()
            .Register(_  => throw new InvalidOperationException("boom"))
            .Register(d  => bag = d);

        var errors = new List<Exception>();
        router.WireAll(FakeChat(), onError: errors.Add);

        Assert.NotNull(bag);                 // second setter ran despite first throwing
        Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(errors[0]);
    }

    [Fact]
    public void WireAll_FailingSetter_NoErrorHandler_SwallowsSilently()
    {
        var router = new AiChatRouter()
            .Register(_ => throw new InvalidOperationException("boom"))
            .Register(_ => { });

        // No exception should propagate even without an onError handler.
        var ex = Record.Exception(() => router.WireAll(FakeChat()));
        Assert.Null(ex);
    }

    // ── RewireExisting ───────────────────────────────────────────────────

    [Fact]
    public void RewireExisting_NoOp_BeforeFirstWire()
    {
        AiChatRouter.ChatDelegate? bag = null;
        var router = new AiChatRouter().Register(d => bag = d);

        router.RewireExisting();

        Assert.Null(bag);
    }

    [Fact]
    public void RewireExisting_PushesLastDelegate_Again()
    {
        var calls = 0;
        var router = new AiChatRouter().Register(_ => calls++);

        router.WireAll(FakeChat());
        Assert.Equal(1, calls);

        router.RewireExisting();
        Assert.Equal(2, calls); // setter invoked again

        router.RewireExisting();
        Assert.Equal(3, calls);
    }

    // ── Integration-shape sanity ────────────────────────────────────────

    [Fact]
    public async Task WiredDelegate_RoundTripsThroughConsumer()
    {
        AiChatRouter.ChatDelegate? consumer = null;
        var router = new AiChatRouter().Register(d => consumer = d);

        router.WireAll(FakeChat("LABEL"));

        var reply = await consumer!("sys-prompt", "user-prompt", default);
        Assert.Equal("LABEL|sys-prompt|user-prompt", reply);
    }
}
