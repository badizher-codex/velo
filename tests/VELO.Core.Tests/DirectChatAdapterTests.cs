using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VELO.Core.AI;
using VELO.Data;
using VELO.Data.Models;
using VELO.Data.Repositories;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// v2.4.42 — coverage for the stateless one-shot adapter that internal AI
/// services use instead of going through AgentLauncher's shared history.
/// FakeHandler lets us assert on the exact HTTP request VELO emits.
/// </summary>
public class DirectChatAdapterTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""",
                    Encoding.UTF8, "application/json"),
            };

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody    = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            CallCount++;
            return Respond(request);
        }
    }

    private static async Task<(DirectChatAdapter Svc, FakeHandler Handler, SettingsRepository Settings)>
        BuildAsync(Action<SettingsRepository>? seed = null)
    {
        var handler = new FakeHandler();
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        var settings = new SettingsRepository(db);
        await settings.SetAsync(SettingKeys.AiMode, "Custom");
        seed?.Invoke(settings);
        var svc = new DirectChatAdapter(settings, logger: null,
            httpFactory: () => new HttpClient(handler));
        return (svc, handler, settings);
    }

    // ── AI mode gating ──────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_returnsEmpty_whenAiModeIsOffline()
    {
        var (svc, handler, _) = await BuildAsync(s =>
            s.SetAsync(SettingKeys.AiMode, "Offline").GetAwaiter().GetResult());

        var reply = await svc.SendAsync("sys", "user", CancellationToken.None);

        Assert.Equal("", reply);
        Assert.Equal(0, handler.CallCount); // No HTTP attempt at all.
    }

    [Fact]
    public async Task SendAsync_returnsEmpty_whenAiModeIsClaude()
    {
        var (svc, handler, _) = await BuildAsync(s =>
            s.SetAsync(SettingKeys.AiMode, "Claude").GetAwaiter().GetResult());

        var reply = await svc.SendAsync("sys", "user", CancellationToken.None);

        Assert.Equal("", reply);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendAsync_returnsEmpty_whenAiModeUnknown()
    {
        var (svc, handler, _) = await BuildAsync(s =>
            s.SetAsync(SettingKeys.AiMode, "Bananas").GetAwaiter().GetResult());

        var reply = await svc.SendAsync("sys", "user", CancellationToken.None);

        Assert.Equal("", reply);
        Assert.Equal(0, handler.CallCount);
    }

    // ── Payload shape — the regression target ───────────────────────────

    [Fact]
    public async Task SendAsync_sendsSystemAndUserAsSeparateMessages_WithCorrectRoles()
    {
        var (svc, handler, _) = await BuildAsync();

        await svc.SendAsync("system text", "user text", CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        // Pin the JSON shape: messages MUST be an array of two entries with role
        // and content fields. This is what LM Studio / Ollama / llama.cpp all expect.
        Assert.Contains("\"messages\":[", handler.LastBody);
        Assert.Contains("\"role\":\"system\"", handler.LastBody);
        Assert.Contains("\"content\":\"system text\"", handler.LastBody);
        Assert.Contains("\"role\":\"user\"", handler.LastBody);
        Assert.Contains("\"content\":\"user text\"", handler.LastBody);
        // Regression: system MUST NOT appear inside the user content (the v2.4.41 bug).
        Assert.DoesNotContain("\"content\":\"system text\\n\\nuser text\"", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_hitsV1ChatCompletions_OnConfiguredEndpoint()
    {
        var (svc, handler, _) = await BuildAsync(s =>
        {
            s.SetAsync(SettingKeys.AiCustomEndpoint, "http://192.168.0.10:1234").GetAwaiter().GetResult();
        });

        await svc.SendAsync("s", "u", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("http://192.168.0.10:1234/v1/chat/completions",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task SendAsync_usesConfiguredModelName()
    {
        var (svc, handler, _) = await BuildAsync(s =>
            s.SetAsync(SettingKeys.AiClaudeModel, "qwen3.6-35b-a3b").GetAwaiter().GetResult());

        await svc.SendAsync("s", "u", CancellationToken.None);

        Assert.Contains("\"model\":\"qwen3.6-35b-a3b\"", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_fallsBackToDefaultModel_whenSettingEmpty()
    {
        var (svc, handler, _) = await BuildAsync();
        // Don't set AiClaudeModel — should default to qwen3:32b.

        await svc.SendAsync("s", "u", CancellationToken.None);

        Assert.Contains("\"model\":\"qwen3:32b\"", handler.LastBody);
    }

    // ── Reply parsing ───────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_returnsAssistantContentFromFirstChoice()
    {
        var (svc, handler, _) = await BuildAsync();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"role":"assistant","content":"BLOCK|0.92|tracker"}}]}""",
                Encoding.UTF8, "application/json"),
        };

        var reply = await svc.SendAsync("s", "u", CancellationToken.None);

        Assert.Equal("BLOCK|0.92|tracker", reply);
    }

    [Fact]
    public async Task ExtractAssistantText_returnsEmptyOnMalformedJson()
    {
        Assert.Equal("", DirectChatAdapter.ExtractAssistantText("not-json"));
        Assert.Equal("", DirectChatAdapter.ExtractAssistantText("{}"));
        Assert.Equal("", DirectChatAdapter.ExtractAssistantText("""{"choices":[]}"""));
        Assert.Equal("", DirectChatAdapter.ExtractAssistantText(
            """{"choices":[{"message":{}}]}"""));
    }

    // ── Fail-soft ───────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_returnsEmpty_OnNon200Status()
    {
        var (svc, handler, _) = await BuildAsync();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("oops"),
        };

        var reply = await svc.SendAsync("s", "u", CancellationToken.None);

        Assert.Equal("", reply);
    }

    [Fact]
    public async Task SendAsync_returnsEmpty_OnTransportException()
    {
        var (svc, handler, _) = await BuildAsync();
        handler.Respond = _ => throw new HttpRequestException("conn refused");

        var reply = await svc.SendAsync("s", "u", CancellationToken.None);

        Assert.Equal("", reply);
    }

    [Fact]
    public async Task SendAsync_propagatesCancellation()
    {
        var (svc, _, _) = await BuildAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.SendAsync("s", "u", cts.Token));
    }

    // ── Concurrency cap ─────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_serializesParallelCallers_OneAtATime()
    {
        // Two callers fire simultaneously. The semaphore must ensure they run
        // sequentially — the second call's HTTP send cannot start until the
        // first has completed.
        var (svc, handler, _) = await BuildAsync();

        var firstStarted = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var inFlight    = 0;
        var maxInFlight = 0;

        handler.Respond = _ => throw new InvalidOperationException(); // not used
        var customHandler = new FakeHandler();
        customHandler.Respond = req =>
        {
            var now = Interlocked.Increment(ref inFlight);
            // Track the high-watermark of concurrent in-flight requests.
            int observed;
            do
            {
                observed = maxInFlight;
                if (now <= observed) break;
            } while (Interlocked.CompareExchange(ref maxInFlight, now, observed) != observed);

            if (req.RequestUri!.Query.Contains("first=1") || handler.LastRequest is null)
            {
                firstStarted.TrySetResult();
                releaseFirst.Task.GetAwaiter().GetResult();
            }
            Interlocked.Decrement(ref inFlight);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"ok"}}]}""",
                    Encoding.UTF8, "application/json"),
            };
        };

        // Build a fresh adapter wired to customHandler.
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        var settings = new SettingsRepository(db);
        await settings.SetAsync(SettingKeys.AiMode, "Custom");
        var concurrentSvc = new DirectChatAdapter(settings, logger: null,
            httpFactory: () => new HttpClient(customHandler));

        var firstTask  = Task.Run(() => concurrentSvc.SendAsync("s1", "u1", CancellationToken.None));
        await firstStarted.Task; // first call is now inside the handler, holding the semaphore
        var secondTask = Task.Run(() => concurrentSvc.SendAsync("s2", "u2", CancellationToken.None));

        // Give the second task a moment to attempt entering — it should be blocked.
        await Task.Delay(150);

        // While first is blocked, only ONE request should be in flight.
        Assert.Equal(1, maxInFlight);

        releaseFirst.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        // After completion the high-watermark is still 1 — they never overlapped.
        Assert.Equal(1, maxInFlight);
        Assert.Equal(2, customHandler.CallCount);
    }

    [Fact]
    public async Task CurrentQueueDepth_isZeroWhenIdle()
    {
        var (svc, _, _) = await BuildAsync();
        Assert.Equal(0, svc.CurrentQueueDepth);
    }
}
