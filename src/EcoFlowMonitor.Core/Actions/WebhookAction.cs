using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Actions;

/// <summary>
/// Executes a single <see cref="WebhookActionData"/>. Implements FR-006,
/// FR-007, FR-018 and the retry semantics captured in spec clarification
/// Q1 (user-configurable <c>retries</c> + <c>retryDelayMs</c>, default 0).
///
/// Returned tuple feeds <see cref="ActionRunner"/>'s audit callback.
/// </summary>
public static class WebhookAction
{
    private static readonly string[] SecretHeaderPrefixes =
    {
        "Authorization",
        "X-*-Token",
        "X-*-Secret",
    };

    public static async Task<(RuleFiringActionOutcome outcome, string? errorText, string? detailJson)>
        RunAsync(
            WebhookActionData cfg,
            DeviceConfig device,
            DeviceState state,
            HttpClient httpClient,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.Url) || !Uri.TryCreate(cfg.Url, UriKind.Absolute, out var uri))
            return (RuleFiringActionOutcome.Failure, "Invalid webhook URL", null);

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return (RuleFiringActionOutcome.Failure, "Webhook URL must be http/https", null);

        var method = cfg.Method?.ToUpperInvariant() switch
        {
            "POST" or null or "" => HttpMethod.Post,
            "PUT"                => HttpMethod.Put,
            _                    => HttpMethod.Post, // unknown → safe default
        };

        var body = cfg.BodyTemplate is { Length: > 0 } bt
            ? TemplateExpander.ExpandString(bt, device, state)
            : BuildDefaultJsonBody(device, state);

        var attempts = Math.Max(1, cfg.Retries + 1);
        var retryDelayMs = Math.Max(100, cfg.RetryDelayMs);
        var timeoutMs    = Math.Max(1000, cfg.TimeoutMs);

        var attemptRecords = new List<Dictionary<string, object?>>();
        var lastErrorText  = (string?)null;
        var overallStart   = Stopwatch.GetTimestamp();

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            using var req = new HttpRequestMessage(method, uri);
            foreach (var kv in cfg.Headers)
            {
                if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                {
                    // Some headers must go on the content instead.
                }
            }
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(timeoutMs);

            HttpStatusCode? status = null;
            string? respBodyExcerpt = null;
            bool timedOut = false;
            string? attemptError = null;

            var attemptStart = Stopwatch.GetTimestamp();
            try
            {
                using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                    .ConfigureAwait(false);
                status = resp.StatusCode;
                try
                {
                    var s = await resp.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
                    respBodyExcerpt = s.Length > 256 ? s.Substring(0, 256) : s;
                }
                catch { /* body read failed; keep status */ }

                if (resp.IsSuccessStatusCode)
                {
                    attemptRecords.Add(MakeAttemptRecord(attempt, (int)status, respBodyExcerpt, null, false, attemptStart));
                    return (RuleFiringActionOutcome.Success, null, SerialiseDetail(uri, cfg, attemptRecords));
                }

                bool retriable = (int)status >= 500 || status == HttpStatusCode.TooManyRequests;
                attemptError = $"HTTP {(int)status}";
                if (!retriable)
                {
                    attemptRecords.Add(MakeAttemptRecord(attempt, (int)status, respBodyExcerpt, attemptError, false, attemptStart));
                    return (RuleFiringActionOutcome.Failure, attemptError, SerialiseDetail(uri, cfg, attemptRecords));
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                attemptError = $"timeout after {timeoutMs}ms";
            }
            catch (HttpRequestException ex)
            {
                attemptError = ex.Message;
            }
            catch (Exception ex)
            {
                attemptError = ex.Message;
            }

            attemptRecords.Add(MakeAttemptRecord(attempt, status.HasValue ? (int)status.Value : -1,
                respBodyExcerpt, attemptError, timedOut, attemptStart));
            lastErrorText = attemptError ?? lastErrorText;

            if (attempt < attempts && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(retryDelayMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        var timedOutFinal = attemptRecords.LastOrDefault()?.GetValueOrDefault("timedOut") is true;
        return (
            timedOutFinal ? RuleFiringActionOutcome.Timeout : RuleFiringActionOutcome.Failure,
            lastErrorText,
            SerialiseDetail(uri, cfg, attemptRecords));
    }

    // -- helpers --

    private static Dictionary<string, object?> MakeAttemptRecord(
        int attempt, int httpStatus, string? respBody, string? error, bool timedOut, long startTimestamp)
    {
        return new Dictionary<string, object?>
        {
            ["attempt"]        = attempt,
            ["httpStatus"]     = httpStatus,
            ["responseBody"]   = respBody,
            ["error"]          = error,
            ["timedOut"]       = timedOut,
            ["durationMs"]     = (int)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
        };
    }

    private static string SerialiseDetail(Uri uri, WebhookActionData cfg, List<Dictionary<string, object?>> attempts)
    {
        return JsonSerializer.Serialize(new
        {
            url      = uri.ToString(),
            method   = cfg.Method,
            // redact secret-looking headers before storing
            headers  = RedactHeaders(cfg.Headers),
            attempts = attempts,
        });
    }

    internal static IReadOnlyDictionary<string, string> RedactHeaders(IDictionary<string, string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            result[kv.Key] = IsSecretHeaderName(kv.Key) ? "***redacted***" : kv.Value;
        }
        return result;
    }

    internal static bool IsSecretHeaderName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) return true;
        // "X-*-Token" / "X-*-Secret" style (case-insensitive)
        if (name.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
        {
            if (name.EndsWith("-Token",    StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("-Secret",   StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("-Key",      StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("-Password", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("-Auth",     StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static string BuildDefaultJsonBody(DeviceConfig device, DeviceState state)
    {
        var inv = CultureInfo.InvariantCulture;
        var obj = new
        {
            @event = new
            {
                ruleId      = (string?)null,  // populated by MonitorOrchestrator if needed
                ruleName    = (string?)null,
                triggerType = state.Power.Status.ToString(),
                isTest      = false,
            },
            device = new
            {
                serialNumber = device.SerialNumber ?? "",
                name         = device.DisplayName,
                batteryPct   = state.Bms?.BatteryPct,
                remainMin    = state.Bms?.RemainMin,
                tempC        = state.Bms?.TempC,
                totalInW     = state.Display?.TotalInW,
                totalOutW    = state.Display?.TotalOutW,
                acPluggedIn  = state.Display?.AcPluggedIn,
                chargeState  = state.Ems?.ChgState,
                powerStatus  = state.Power.Status.ToString(),
            },
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", inv),
        };
        return JsonSerializer.Serialize(obj);
    }
}
