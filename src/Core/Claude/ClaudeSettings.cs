using System;
using System.IO;
using System.Text.Json;

namespace CivilAI.Core.Claude
{
    /// <summary>
    /// How the plugin reaches Claude (D2: each user's own Anthropic API key).
    /// The key comes from the environment or the user's settings file, never from the repo.
    /// </summary>
    public sealed class ClaudeSettings
    {
        /// <summary>Default model; override with <c>CIVILAI_CLAUDE_MODEL</c> or <c>"model"</c> in the settings file.</summary>
        public const string DefaultModel = "claude-sonnet-4-5";
        public const string DefaultBaseUrl = "https://api.anthropic.com";
        public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
        public const string ModelVariable = "CIVILAI_CLAUDE_MODEL";

        public string ApiKey { get; init; } = "";
        public string Model { get; init; } = DefaultModel;
        public string BaseUrl { get; init; } = DefaultBaseUrl;
        public int MaxTokens { get; init; } = 8192;
        /// <summary>Retries after the first attempt for rate limits, overload and server errors.</summary>
        public int MaxRetries { get; init; } = 3;
        public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>Per-user settings file: %APPDATA%\CivilAI\settings.json (~/.config/CivilAI on Linux).</summary>
        public static string DefaultSettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivilAI", "settings.json");

        /// <summary>
        /// Loads settings: values from the settings file (if it exists), then environment variables
        /// override them. <paramref name="getEnv"/> and <paramref name="settingsPath"/> exist for tests.
        /// </summary>
        public static ClaudeSettings Load(Func<string, string?>? getEnv = null, string? settingsPath = null)
        {
            getEnv ??= Environment.GetEnvironmentVariable;
            settingsPath ??= DefaultSettingsPath;

            string apiKey = "", model = DefaultModel, baseUrl = DefaultBaseUrl;
            int maxTokens = 8192, maxRetries = 3;

            if (File.Exists(settingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"{settingsPath}: settings must be a JSON object.");
                apiKey = ReadString(root, "anthropicApiKey") ?? apiKey;
                model = ReadString(root, "model") ?? model;
                baseUrl = ReadString(root, "baseUrl") ?? baseUrl;
                maxTokens = ReadInt(root, "maxTokens") ?? maxTokens;
                maxRetries = ReadInt(root, "maxRetries") ?? maxRetries;
            }

            var envKey = getEnv(ApiKeyVariable);
            if (!string.IsNullOrWhiteSpace(envKey)) apiKey = envKey.Trim();
            var envModel = getEnv(ModelVariable);
            if (!string.IsNullOrWhiteSpace(envModel)) model = envModel.Trim();

            if (maxTokens <= 0) throw new FormatException("maxTokens must be positive.");
            if (maxRetries < 0) throw new FormatException("maxRetries must not be negative.");

            return new ClaudeSettings
            {
                ApiKey = apiKey.Trim(),
                Model = model,
                BaseUrl = baseUrl.TrimEnd('/'),
                MaxTokens = maxTokens,
                MaxRetries = maxRetries,
            };
        }

        private static string? ReadString(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()
                : null;

        private static int? ReadInt(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

        /// <summary>Never prints the key.</summary>
        public override string ToString() =>
            $"Claude(model={Model}, baseUrl={BaseUrl}, apiKey={(HasApiKey ? "set" : "missing")})";
    }
}
