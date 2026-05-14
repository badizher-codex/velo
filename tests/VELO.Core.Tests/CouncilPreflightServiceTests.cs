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
/// Phase 4.0 chunk F — coverage for the Council pre-flight probe.
/// Uses a fake HttpMessageHandler so the tests run offline and deterministic.
/// </summary>
public class CouncilPreflightServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Respond(request));
        }
    }

    private static (CouncilPreflightService Svc, FakeHandler Handler) BuildSvc(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new FakeHandler();
        if (respond != null) handler.Respond = respond;

        // SettingsRepository requires a VeloDatabase backed by a real file.
        // Use a unique temp folder per test so they don't share state.
        var tempFolder = Path.Combine(
            Path.GetTempPath(),
            "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        db.InitializeAsync().GetAwaiter().GetResult();
        var settings = new SettingsRepository(db);

        var svc = new CouncilPreflightService(
            settings,
            logger: null,
            httpFactory: () => new HttpClient(handler));
        return (svc, handler);
    }

    private static HttpResponseMessage JsonOk(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task EndpointUnreachable_returnsAllFalse()
    {
        var (svc, _) = BuildSvc(_ => throw new HttpRequestException("conn refused"));

        var result = await svc.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.False(result.EndpointReachable);
        Assert.False(result.ModelPresent);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task TagsReturnsNon200_reportsUnreachable()
    {
        var (svc, _) = BuildSvc(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await svc.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.False(result.EndpointReachable);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ModelMissing_reportsModelNotPresent()
    {
        var tagsBody = """{"models":[{"name":"llama3.1:8b"},{"name":"phi3:mini"}]}""";
        var (svc, _) = BuildSvc(req =>
            req.RequestUri!.PathAndQuery.EndsWith("/api/tags") ? JsonOk(tagsBody)
                                                              : new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await svc.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.True(result.EndpointReachable);
        Assert.False(result.ModelPresent);
        Assert.Contains("qwen3:32b", result.FailureReason);
    }

    [Fact]
    public async Task ModelPresent_contextOk_reportsHealthy()
    {
        var tagsBody = """{"models":[{"name":"qwen3:32b"},{"name":"llama3.1:8b"}]}""";
        var showBody = """{"parameters":{"num_ctx":16384}}""";
        var (svc, _) = BuildSvc(req =>
            req.RequestUri!.PathAndQuery.EndsWith("/api/tags") ? JsonOk(tagsBody) :
            req.RequestUri!.PathAndQuery.EndsWith("/api/show") ? JsonOk(showBody) :
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await svc.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.True(result.EndpointReachable);
        Assert.True(result.ModelPresent);
        Assert.Equal(16384, result.ContextSize);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task ModelPresent_contextTooSmall_reportsUnhealthy()
    {
        var tagsBody = """{"models":[{"name":"qwen3:32b"}]}""";
        var showBody = """{"parameters":{"num_ctx":4096}}""";
        var (svc, _) = BuildSvc(req =>
            req.RequestUri!.PathAndQuery.EndsWith("/api/tags") ? JsonOk(tagsBody) :
            req.RequestUri!.PathAndQuery.EndsWith("/api/show") ? JsonOk(showBody) :
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await svc.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.True(result.ModelPresent);
        Assert.Equal(4096, result.ContextSize);
        Assert.Contains("contexto", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowEndpointFails_butModelPresent_stillHealthyWithNullContext()
    {
        var tagsBody = """{"models":[{"name":"qwen3:32b"}]}""";
        var (svc, _) = BuildSvc(req =>
            req.RequestUri!.PathAndQuery.EndsWith("/api/tags") ? JsonOk(tagsBody) :
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await svc.CheckAsync();

        // /api/show failing is non-fatal — model is present, context unknown.
        Assert.True(result.IsHealthy);
        Assert.True(result.ModelPresent);
        Assert.Null(result.ContextSize);
    }

    [Fact]
    public async Task DefaultEndpoint_isOllamaCanonical11434_NotLmStudio1234()
    {
        // v2.4.39 regression test — Council must default to Ollama's canonical port,
        // not piggyback on AiCustomEndpoint (which the user may have legitimately
        // configured for LM Studio on 1234 or another OpenAI-compatible server).
        string? hitUrl = null;
        var (svc, _) = BuildSvc(req =>
        {
            hitUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"models":[]}""", Encoding.UTF8, "application/json"),
            };
        });

        await svc.CheckAsync();

        Assert.NotNull(hitUrl);
        Assert.Contains("localhost:11434", hitUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":1234/", hitUrl);
    }

    [Fact]
    public async Task CustomEndpoint_fromCouncilSpecificSetting_overridesDefault()
    {
        // Ensure the service reads SettingKeys.CouncilOllamaEndpoint (not AiCustomEndpoint).
        // Use a real SettingsRepository because the service goes through it.
        string? hitUrl = null;
        var handler = new FakeHandler
        {
            Respond = req =>
            {
                hitUrl = req.RequestUri!.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"models":[]}""", Encoding.UTF8, "application/json"),
                };
            },
        };
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        var settings = new SettingsRepository(db);
        await settings.SetAsync(SettingKeys.CouncilOllamaEndpoint, "http://192.168.1.50:11434");
        // Set AiCustomEndpoint to something different — must NOT be used.
        await settings.SetAsync(SettingKeys.AiCustomEndpoint, "http://localhost:1234");

        var svc = new CouncilPreflightService(settings, logger: null,
            httpFactory: () => new HttpClient(handler));

        await svc.CheckAsync();

        Assert.NotNull(hitUrl);
        Assert.Contains("192.168.1.50:11434", hitUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":1234/", hitUrl);
    }

    // ── v2.4.40: dual-backend support tests ──────────────────────────────

    /// <summary>Spins up a service backed by a real SettingsRepository so the test can
    /// pre-seed CouncilBackendType / CouncilModeratorModel. Returns the service + the
    /// handler so the test can inspect the request the probe issued.</summary>
    private static async Task<(CouncilPreflightService Svc, FakeHandler Handler)> BuildSvcWithSettings(
        Action<SettingsRepository> seed,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler { Respond = respond };
        var tempFolder = Path.Combine(Path.GetTempPath(), "velo-test-" + Guid.NewGuid().ToString("N"));
        var db = new VeloDatabase(NullLogger<VeloDatabase>.Instance, tempFolder);
        await db.InitializeAsync();
        var settings = new SettingsRepository(db);
        seed(settings);
        var svc = new CouncilPreflightService(settings, logger: null,
            httpFactory: () => new HttpClient(handler));
        return (svc, handler);
    }

    [Fact]
    public async Task LMStudioBackend_probesV1Models_NotApiTags()
    {
        // v2.4.40 — LM Studio exposes OpenAI-compat /v1/models, not Ollama /api/tags.
        // Ensure the probe routes to the right endpoint based on backend setting.
        var probedPaths = new List<string>();
        var (svc, _) = await BuildSvcWithSettings(
            s =>
            {
                s.SetAsync(SettingKeys.CouncilBackendType, "LMStudio").GetAwaiter().GetResult();
                s.SetAsync(SettingKeys.CouncilOllamaEndpoint, "http://localhost:1234").GetAwaiter().GetResult();
                s.SetAsync(SettingKeys.CouncilModeratorModel, "qwen3.6-35b-a3b").GetAwaiter().GetResult();
            },
            req =>
            {
                probedPaths.Add(req.RequestUri!.AbsolutePath);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"data":[{"id":"qwen3.6-35b-a3b","object":"model"}]}""",
                        Encoding.UTF8, "application/json"),
                };
            });

        var result = await svc.CheckAsync();

        Assert.Contains("/v1/models", probedPaths);
        Assert.DoesNotContain("/api/tags", probedPaths);
        Assert.True(result.IsHealthy);
        Assert.Equal(CouncilPreflightService.Backend.LMStudio, result.BackendType);
        Assert.Equal("qwen3.6-35b-a3b", result.ModelName);
        Assert.Null(result.ContextSize); // OpenAI-compat /v1/models doesn't expose context size.
    }

    [Fact]
    public async Task LMStudioBackend_modelMissing_reportsModelNotPresent()
    {
        var (svc, _) = await BuildSvcWithSettings(
            s =>
            {
                s.SetAsync(SettingKeys.CouncilBackendType, "LMStudio").GetAwaiter().GetResult();
                s.SetAsync(SettingKeys.CouncilModeratorModel, "qwen3:32b").GetAwaiter().GetResult();
            },
            req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"id":"llama3-8b-instruct"}]}""",
                    Encoding.UTF8, "application/json"),
            });

        var result = await svc.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.True(result.EndpointReachable);
        Assert.False(result.ModelPresent);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("LM Studio", result.FailureReason);
    }

    [Fact]
    public async Task OpenAICompatBackend_endpointDown_reportsUnreachable()
    {
        var (svc, _) = await BuildSvcWithSettings(
            s => s.SetAsync(SettingKeys.CouncilBackendType, "OpenAICompat").GetAwaiter().GetResult(),
            _ => throw new HttpRequestException("conn refused"));

        var result = await svc.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.False(result.EndpointReachable);
        Assert.Equal(CouncilPreflightService.Backend.OpenAICompat, result.BackendType);
    }

    [Fact]
    public async Task CustomModeratorModelName_isUsedInsteadOfDefault()
    {
        // Spec default is qwen3:32b but user can override (e.g. qwen3.6-35b-a3b for LM Studio).
        // Verify the override is reflected in the Result's ModelName AND used in the lookup.
        var (svc, _) = await BuildSvcWithSettings(
            s =>
            {
                s.SetAsync(SettingKeys.CouncilModeratorModel, "qwen2.5:72b").GetAwaiter().GetResult();
            },
            req => req.RequestUri!.AbsolutePath.EndsWith("/api/tags")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                  {
                      Content = new StringContent("""{"models":[{"name":"qwen2.5:72b"}]}""",
                          Encoding.UTF8, "application/json"),
                  }
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await svc.CheckAsync();

        Assert.True(result.ModelPresent);
        Assert.Equal("qwen2.5:72b", result.ModelName);
    }
}
