using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Claude;
using CivilAI.Core.Intent;
using Xunit;

namespace CivilAI.Core.Tests.Claude
{
    public class ClaudeClientTests
    {
        /// <summary>Replays queued responses and records requests; no network.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses = new();
            public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

            public FakeHandler Reply(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
            {
                _responses.Enqueue(() =>
                {
                    var r = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                    configure?.Invoke(r);
                    return r;
                });
                return this;
            }

            public FakeHandler Fail()
            {
                _responses.Enqueue(() => throw new HttpRequestException("connection reset"));
                return this;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                Requests.Add((request, body));
                return _responses.Dequeue()();
            }
        }

        private const string TextReply =
            "{\"id\":\"msg_1\",\"type\":\"message\",\"model\":\"m\",\"stop_reason\":\"end_turn\"," +
            "\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}],\"usage\":{\"input_tokens\":12,\"output_tokens\":3}}";

        private const string ToolReply =
            "{\"id\":\"msg_2\",\"model\":\"m\",\"stop_reason\":\"tool_use\",\"content\":[" +
            "{\"type\":\"text\",\"text\":\"Here is the intent.\"}," +
            "{\"type\":\"tool_use\",\"id\":\"tu_1\",\"name\":\"design_intent\",\"input\":{\"schemaVersion\":\"0\",\"spaces\":[]}}]," +
            "\"usage\":{\"input_tokens\":100,\"output_tokens\":50}}";

        private static readonly ClaudeSettings Settings = new() { ApiKey = "sk-test", Model = "test-model", MaxRetries = 2 };

        private static (ClaudeClient Client, FakeHandler Handler, List<TimeSpan> Delays) Make(FakeHandler handler)
        {
            var delays = new List<TimeSpan>();
            var client = new ClaudeClient(Settings, new HttpClient(handler), (d, _) => { delays.Add(d); return Task.CompletedTask; });
            return (client, handler, delays);
        }

        private static ClaudeRequest Hello() => new() { Content = new[] { ClaudeContent.FromText("hi") } };

        private static JsonOutputSpec IntentSpec() =>
            new("design_intent", "Return the Design Intent.", DesignIntentJson.ReadSchema());

        [Fact]
        public async Task Sends_headers_and_parses_text_reply()
        {
            var (client, handler, _) = Make(new FakeHandler().Reply(HttpStatusCode.OK, TextReply));
            var response = await client.SendAsync(Hello());

            Assert.Equal("Hello", response.Text);
            Assert.Equal(12, response.InputTokens);
            Assert.Equal(3, response.OutputTokens);
            Assert.Null(response.Json);

            var (req, body) = handler.Requests.Single();
            Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
            Assert.Equal("sk-test", req.Headers.GetValues("x-api-key").Single());
            Assert.Equal(ClaudeClient.ApiVersion, req.Headers.GetValues("anthropic-version").Single());
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("test-model", doc.RootElement.GetProperty("model").GetString());
            Assert.Equal(8192, doc.RootElement.GetProperty("max_tokens").GetInt32());
        }

        [Fact]
        public void Body_contains_system_text_and_base64_image()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            var request = new ClaudeRequest
            {
                System = "You are a landscape design assistant.",
                Content = new[] { ClaudeContent.FromImage(png, "image/png"), ClaudeContent.FromText("Read this sketch.") },
                MaxTokens = 1000,
            };
            var body = ClaudeClient.BuildBody(request, Settings);
            using var doc = JsonDocument.Parse(body.ToJsonString());
            var root = doc.RootElement;

            Assert.Equal("You are a landscape design assistant.", root.GetProperty("system").GetString());
            Assert.Equal(1000, root.GetProperty("max_tokens").GetInt32());
            var content = root.GetProperty("messages")[0].GetProperty("content");
            Assert.Equal("image", content[0].GetProperty("type").GetString());
            Assert.Equal("image/png", content[0].GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal(Convert.ToBase64String(png), content[0].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("Read this sketch.", content[1].GetProperty("text").GetString());
            Assert.False(root.TryGetProperty("tools", out _));
        }

        [Fact]
        public void Json_output_forces_tool_with_design_intent_schema()
        {
            var request = new ClaudeRequest { Content = new[] { ClaudeContent.FromText("a shaded plaza") }, JsonOutput = IntentSpec() };
            using var doc = JsonDocument.Parse(ClaudeClient.BuildBody(request, Settings).ToJsonString());
            var root = doc.RootElement;

            var tool = root.GetProperty("tools")[0];
            Assert.Equal("design_intent", tool.GetProperty("name").GetString());
            var schema = tool.GetProperty("input_schema");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.False(schema.TryGetProperty("$schema", out _));
            Assert.False(schema.TryGetProperty("$id", out _));
            Assert.Equal("tool", root.GetProperty("tool_choice").GetProperty("type").GetString());
            Assert.Equal("design_intent", root.GetProperty("tool_choice").GetProperty("name").GetString());
        }

        [Fact]
        public async Task Structured_reply_returns_tool_input_json()
        {
            var (client, _, _) = Make(new FakeHandler().Reply(HttpStatusCode.OK, ToolReply));
            var response = await client.SendAsync(new ClaudeRequest { Content = new[] { ClaudeContent.FromText("x") }, JsonOutput = IntentSpec() });

            Assert.NotNull(response.Json);
            using var doc = JsonDocument.Parse(response.Json!);
            Assert.Equal("0", doc.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("Here is the intent.", response.Text);
        }

        [Fact]
        public void Missing_structured_result_after_max_tokens_says_so()
        {
            const string cut = "{\"id\":\"m\",\"stop_reason\":\"max_tokens\",\"content\":[{\"type\":\"text\",\"text\":\"...\"}]}";
            var ex = Assert.Throws<ClaudeApiException>(() => ClaudeClient.ParseResponse(cut, IntentSpec()));
            Assert.Contains("cut off", ex.Message);
        }

        [Fact]
        public async Task Retries_rate_limit_and_overload_then_succeeds()
        {
            var handler = new FakeHandler()
                .Reply((HttpStatusCode)429, "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"slow down\"}}",
                       r => r.Headers.TryAddWithoutValidation("retry-after", "7"))
                .Reply((HttpStatusCode)529, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"busy\"}}")
                .Reply(HttpStatusCode.OK, TextReply);
            var (client, _, delays) = Make(handler);

            var response = await client.SendAsync(Hello());

            Assert.Equal("Hello", response.Text);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(2, delays.Count);
            Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);                     // server's retry-after
            Assert.True(delays[1] >= TimeSpan.FromSeconds(2) && delays[1] <= TimeSpan.FromSeconds(2.5)); // backoff + jitter
        }

        [Fact]
        public async Task Retries_transport_errors()
        {
            var handler = new FakeHandler().Fail().Reply(HttpStatusCode.OK, TextReply);
            var (client, _, delays) = Make(handler);
            var response = await client.SendAsync(Hello());
            Assert.Equal("Hello", response.Text);
            Assert.Single(delays);
        }

        [Fact]
        public async Task Gives_up_after_max_retries()
        {
            var handler = new FakeHandler();
            for (int i = 0; i < 3; i++) handler.Reply(HttpStatusCode.ServiceUnavailable, "{}");
            var (client, _, _) = Make(handler);

            var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => client.SendAsync(Hello()));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.Contains("3 attempts", ex.Message);
            Assert.Equal(3, handler.Requests.Count);
        }

        [Fact]
        public async Task Does_not_retry_bad_key()
        {
            var handler = new FakeHandler().Reply(HttpStatusCode.Unauthorized,
                "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
            var (client, _, delays) = Make(handler);

            var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => client.SendAsync(Hello()));
            Assert.Equal("authentication_error", ex.ErrorType);
            Assert.Contains("invalid x-api-key", ex.Message);
            Assert.Single(handler.Requests);
            Assert.Empty(delays);
        }

        [Fact]
        public void Client_without_key_explains_where_to_set_it()
        {
            var ex = Assert.Throws<ClaudeApiException>(() => new ClaudeClient(new ClaudeSettings()));
            Assert.Contains(ClaudeSettings.ApiKeyVariable, ex.Message);
        }

        [Theory]
        [InlineData("sketch.PNG", "image/png")]
        [InlineData("render.jpg", "image/jpeg")]
        [InlineData("mood.jpeg", "image/jpeg")]
        [InlineData("ref.webp", "image/webp")]
        public void Media_type_from_extension(string file, string expected) =>
            Assert.Equal(expected, ClaudeContent.MediaTypeFromExtension(file));

        [Fact]
        public void Rejects_unsupported_or_oversized_images()
        {
            Assert.Throws<ArgumentException>(() => ClaudeContent.MediaTypeFromExtension("plan.pdf"));
            Assert.Throws<ArgumentException>(() => ClaudeContent.FromImage(new byte[] { 1 }, "image/tiff"));
            Assert.Throws<ArgumentException>(() => ClaudeContent.FromImage(new byte[ClaudeContent.MaxImageBytes + 1], "image/png"));
        }
    }

    public class ClaudeSettingsTests
    {
        private static string TempFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), $"civilai-settings-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, content);
            return path;
        }

        private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
            name => vars.FirstOrDefault(v => v.Key == name).Value;

        [Fact]
        public void Defaults_when_nothing_is_set()
        {
            var s = ClaudeSettings.Load(Env(), Path.Combine(Path.GetTempPath(), "does-not-exist-civilai.json"));
            Assert.False(s.HasApiKey);
            Assert.Equal(ClaudeSettings.DefaultModel, s.Model);
            Assert.Equal(ClaudeSettings.DefaultBaseUrl, s.BaseUrl);
        }

        [Fact]
        public void Reads_settings_file_and_environment_overrides_it()
        {
            var path = TempFile("{ // user settings\n \"anthropicApiKey\": \"sk-file\", \"model\": \"file-model\", \"maxTokens\": 4000, }");
            try
            {
                var fromFile = ClaudeSettings.Load(Env(), path);
                Assert.Equal("sk-file", fromFile.ApiKey);
                Assert.Equal("file-model", fromFile.Model);
                Assert.Equal(4000, fromFile.MaxTokens);

                var withEnv = ClaudeSettings.Load(Env((ClaudeSettings.ApiKeyVariable, " sk-env "), (ClaudeSettings.ModelVariable, "env-model")), path);
                Assert.Equal("sk-env", withEnv.ApiKey);
                Assert.Equal("env-model", withEnv.Model);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ToString_never_shows_the_key()
        {
            var s = new ClaudeSettings { ApiKey = "sk-secret-123" };
            Assert.DoesNotContain("sk-secret", s.ToString());
            Assert.Contains("apiKey=set", s.ToString());
        }
    }
}
