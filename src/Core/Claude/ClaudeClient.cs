using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CivilAI.Core.Claude
{
    /// <summary>Claude API error the caller should show to the designer (bad key, bad request, gave up retrying).</summary>
    public sealed class ClaudeApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; }
        /// <summary>Anthropic error type, e.g. authentication_error, rate_limit_error, overloaded_error.</summary>
        public string? ErrorType { get; }

        public ClaudeApiException(string message, HttpStatusCode? statusCode = null, string? errorType = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ErrorType = errorType;
        }
    }

    /// <summary>
    /// Minimal Messages API client (POST /v1/messages) on plain HttpClient + System.Text.Json,
    /// so Core has no package dependencies. Used by the palette mode (D9 mode 1); the connector
    /// mode does not need it because Claude itself is the caller there.
    /// </summary>
    public sealed class ClaudeClient
    {
        public const string ApiVersion = "2023-06-01";

        private readonly HttpClient _http;
        private readonly ClaudeSettings _settings;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Random _jitter = new Random();

        /// <param name="delay">Waits between retries; replaced in tests.</param>
        public ClaudeClient(ClaudeSettings settings, HttpClient? http = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (!settings.HasApiKey)
                throw new ClaudeApiException(
                    $"No Anthropic API key. Set {ClaudeSettings.ApiKeyVariable} or \"anthropicApiKey\" in {ClaudeSettings.DefaultSettingsPath}.");
            _http = http ?? new HttpClient { Timeout = settings.Timeout };
            _delay = delay ?? Task.Delay;
        }

        public async Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (request.Content.Count == 0) throw new ArgumentException("Request has no content.", nameof(request));

            var body = BuildBody(request, _settings).ToJsonString();

            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response;
                try
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, _settings.BaseUrl + "/v1/messages")
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                    message.Headers.Add("x-api-key", _settings.ApiKey);
                    message.Headers.Add("anthropic-version", ApiVersion);
                    response = await _http.SendAsync(message, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransportError(ex, ct))
                {
                    if (attempt >= _settings.MaxRetries)
                        throw new ClaudeApiException($"Could not reach Claude after {attempt + 1} attempts: {ex.Message}", inner: ex);
                    await _delay(Backoff(attempt, null), ct).ConfigureAwait(false);
                    continue;
                }

                using (response)
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return ParseResponse(text, request.JsonOutput);

                    var (type, msg) = ParseError(text);
                    if (IsRetryable(response.StatusCode) && attempt < _settings.MaxRetries)
                    {
                        await _delay(Backoff(attempt, RetryAfter(response)), ct).ConfigureAwait(false);
                        continue;
                    }
                    var tried = IsRetryable(response.StatusCode) ? $" (gave up after {attempt + 1} attempts)" : "";
                    throw new ClaudeApiException(
                        $"Claude API {(int)response.StatusCode} {type ?? ""}: {msg ?? response.ReasonPhrase}{tried}".Replace("  ", " "),
                        response.StatusCode, type);
                }
            }
        }

        /// <summary>Builds the JSON body of a Messages API request.</summary>
        public static JsonObject BuildBody(ClaudeRequest request, ClaudeSettings settings)
        {
            var content = new JsonArray();
            foreach (var part in request.Content)
            {
                if (part.IsImage)
                    content.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = part.MediaType,
                            ["data"] = Convert.ToBase64String(part.ImageData!),
                        },
                    });
                else
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = part.Text });
            }

            var body = new JsonObject
            {
                ["model"] = settings.Model,
                ["max_tokens"] = request.MaxTokens ?? settings.MaxTokens,
                ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content } },
            };
            if (!string.IsNullOrEmpty(request.System)) body["system"] = request.System;
            if (request.Temperature is double t) body["temperature"] = t;

            if (request.JsonOutput is JsonOutputSpec spec)
            {
                var schema = JsonNode.Parse(spec.SchemaJson)!.AsObject();
                // Document-level keywords are not part of the tool's input shape.
                schema.Remove("$schema");
                schema.Remove("$id");
                body["tools"] = new JsonArray
                {
                    new JsonObject { ["name"] = spec.ToolName, ["description"] = spec.Description, ["input_schema"] = schema },
                };
                body["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = spec.ToolName };
            }
            return body;
        }

        /// <summary>Parses a successful Messages API response.</summary>
        public static ClaudeResponse ParseResponse(string json, JsonOutputSpec? jsonOutput)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var text = new StringBuilder();
            string? toolJson = null;

            if (root.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in blocks.EnumerateArray())
                {
                    var type = Str(block, "type");
                    if (type == "text")
                    {
                        if (text.Length > 0) text.Append('\n');
                        text.Append(Str(block, "text"));
                    }
                    else if (type == "tool_use" && jsonOutput is not null && toolJson is null
                             && Str(block, "name") == jsonOutput.ToolName && block.TryGetProperty("input", out var input))
                    {
                        toolJson = input.GetRawText();
                    }
                }
            }

            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("input_tokens", out var i) && i.TryGetInt32(out var iv)) inTok = iv;
                if (usage.TryGetProperty("output_tokens", out var o) && o.TryGetInt32(out var ov)) outTok = ov;
            }

            var result = new ClaudeResponse
            {
                Id = Str(root, "id"),
                Model = Str(root, "model"),
                StopReason = Str(root, "stop_reason"),
                Text = text.ToString(),
                Json = toolJson,
                InputTokens = inTok,
                OutputTokens = outTok,
            };

            if (jsonOutput is not null && toolJson is null)
                throw new ClaudeApiException(result.WasTruncated
                    ? "Claude's answer was cut off before the structured result was complete (max_tokens). Try a shorter brief or raise maxTokens."
                    : $"Claude did not return the structured result \"{jsonOutput.ToolName}\".");
            return result;
        }

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static (string? type, string? message) ParseError(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                    return (Str(err, "type"), Str(err, "message"));
            }
            catch (JsonException) { }
            return (null, null);
        }

        /// <summary>408, 409, 429 (rate limit), 5xx and 529 (overloaded) are worth retrying.</summary>
        public static bool IsRetryable(HttpStatusCode status)
        {
            int code = (int)status;
            return code == 408 || code == 409 || code == 429 || code >= 500;
        }

        private static bool IsTransportError(Exception ex, CancellationToken ct) =>
            ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested); // timeout

        private static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter is { } ra)
            {
                if (ra.Delta is TimeSpan d) return d;
                if (ra.Date is DateTimeOffset date) return date - DateTimeOffset.UtcNow;
            }
            if (response.Headers.TryGetValues("retry-after", out var values))
                foreach (var v in values)
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return TimeSpan.FromSeconds(s);
            return null;
        }

        /// <summary>Server's retry-after when given (capped at 60 s), else 1 s, 2 s, 4 s ... plus up to 25 % jitter.</summary>
        private TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
        {
            if (retryAfter is TimeSpan ra && ra > TimeSpan.Zero)
                return ra > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : ra;
            double seconds = Math.Min(30, Math.Pow(2, attempt));
            double jitter;
            lock (_jitter) jitter = _jitter.NextDouble() * 0.25;
            return TimeSpan.FromSeconds(seconds * (1 + jitter));
        }
    }
}
