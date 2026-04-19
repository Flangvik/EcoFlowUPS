using System.Net;
using System.Net.Http;
using System.Text.Json;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Client;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;
using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests;

public class WebhookActionTests
{
    private static (DeviceConfig, DeviceState) Fixture()
    {
        var d = new DeviceConfig { DisplayName = "Test Station", SerialNumber = "SN-W" };
        var s = new DeviceState
        {
            Bms = new BmsData { BatteryPct = 50f },
            Display = new DisplayData { TotalInW = 0, TotalOutW = 100 },
            Power = new PowerState { Status = PowerStatus.PowerLost },
        };
        return (d, s);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _f;
        public int Calls { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = new();
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> f) => _f = f;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Calls++;
            Requests.Add(req);
            return await _f(req, ct);
        }
    }

    private static HttpClient Client(FakeHandler h) => new(h);

    [Fact]
    public async Task HappyPath_SingleAttempt_Success()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        }));
        var cfg = new WebhookActionData { Url = "http://localhost:0/hook", Retries = 0 };

        var (outcome, err, detail) = await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        outcome.Should().Be(RuleFiringActionOutcome.Success);
        err.Should().BeNull();
        handler.Calls.Should().Be(1);
        detail.Should().Contain("\"httpStatus\":200");
    }

    [Fact]
    public async Task Retry_ExhaustsThenFails_On503()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("busy")
        }));
        var cfg = new WebhookActionData { Url = "http://localhost:0/hook", Retries = 2, RetryDelayMs = 100 };

        var (outcome, err, detail) = await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        outcome.Should().Be(RuleFiringActionOutcome.Failure);
        err.Should().Be("HTTP 503");
        handler.Calls.Should().Be(3); // initial + 2 retries
    }

    [Fact]
    public async Task NonRetriableStatus_404_DoesNotRetry()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var cfg = new WebhookActionData { Url = "http://localhost:0/hook", Retries = 5, RetryDelayMs = 50 };

        var (outcome, _, _) = await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        outcome.Should().Be(RuleFiringActionOutcome.Failure);
        handler.Calls.Should().Be(1); // 404 is non-retriable
    }

    [Fact]
    public async Task TransientStatus_429_IsRetried()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)));
        var cfg = new WebhookActionData { Url = "http://localhost:0/hook", Retries = 2, RetryDelayMs = 50 };

        await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        handler.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Timeout_KillsAttempt_ReturnsTimeoutOutcome()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler(async (_, ct) =>
        {
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var cfg = new WebhookActionData { Url = "http://localhost:0/hook", Retries = 0, TimeoutMs = 200 };

        var (outcome, err, _) = await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        outcome.Should().Be(RuleFiringActionOutcome.Timeout);
        err.Should().Contain("timeout");
    }

    [Fact]
    public async Task Headers_AreSentVerbatim_ButRedactedInDetail()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var cfg = new WebhookActionData
        {
            Url = "http://localhost:0/hook",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer abc123",
                ["X-Foo"]         = "bar",
                ["X-Trace-Token"] = "tracey",
            },
        };

        var (_, _, detail) = await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        // Actual HTTP request got the unredacted value:
        handler.Requests[0].Headers.Authorization!.ToString().Should().Be("Bearer abc123");

        // But the stored audit detail redacted secrets:
        using var doc = JsonDocument.Parse(detail!);
        var headers = doc.RootElement.GetProperty("headers");
        headers.GetProperty("Authorization").GetString().Should().Be("***redacted***");
        headers.GetProperty("X-Foo").GetString().Should().Be("bar");
        headers.GetProperty("X-Trace-Token").GetString().Should().Be("***redacted***");
    }

    [Fact]
    public async Task InvalidUrl_FailsImmediately_WithoutNetworkCall()
    {
        var (d, s) = Fixture();
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var cfg = new WebhookActionData { Url = "not-a-url" };

        var (outcome, err, _) = await WebhookAction.RunAsync(cfg, d, s, Client(handler), default);

        outcome.Should().Be(RuleFiringActionOutcome.Failure);
        err.Should().Contain("URL");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public void IsSecretHeaderName_MatchesCommonPatterns()
    {
        WebhookAction.IsSecretHeaderName("Authorization").Should().BeTrue();
        WebhookAction.IsSecretHeaderName("authorization").Should().BeTrue();
        WebhookAction.IsSecretHeaderName("X-API-Token").Should().BeTrue();
        WebhookAction.IsSecretHeaderName("X-Webhook-Secret").Should().BeTrue();
        WebhookAction.IsSecretHeaderName("X-Api-Key").Should().BeTrue();
        WebhookAction.IsSecretHeaderName("X-Trace-Id").Should().BeFalse();
        WebhookAction.IsSecretHeaderName("Content-Type").Should().BeFalse();
    }
}
