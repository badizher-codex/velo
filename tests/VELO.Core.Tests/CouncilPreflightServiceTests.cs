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
}
