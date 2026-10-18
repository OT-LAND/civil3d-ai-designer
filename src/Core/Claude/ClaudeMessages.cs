using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CivilAI.Core.Claude
{
    /// <summary>One part of a user message: text or an image (sketch, site plan, reference render).</summary>
    public sealed class ClaudeContent
    {
        /// <summary>Anthropic API limit per image (base64 source).</summary>
        public const int MaxImageBytes = 5 * 1024 * 1024;

        private static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

        public string? Text { get; }
        public byte[]? ImageData { get; }
        public string? MediaType { get; }
        public bool IsImage => ImageData is not null;

        private ClaudeContent(string? text, byte[]? imageData, string? mediaType)
        {
            Text = text;
            ImageData = imageData;
            MediaType = mediaType;
        }

        public static ClaudeContent FromText(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Text is empty.", nameof(text));
            return new ClaudeContent(text, null, null);
        }

        public static ClaudeContent FromImage(byte[] data, string mediaType)
        {
            if (data is null || data.Length == 0) throw new ArgumentException("Image is empty.", nameof(data));
            if (Array.IndexOf(ImageTypes, mediaType) < 0)
                throw new ArgumentException($"Unsupported image type \"{mediaType}\". Use JPEG, PNG, GIF or WebP.", nameof(mediaType));
            if (data.Length > MaxImageBytes)
                throw new ArgumentException($"Image is {data.Length / (1024.0 * 1024.0):0.0} MB; the limit is 5 MB. Export it smaller or as JPEG.", nameof(data));
            return new ClaudeContent(null, data, mediaType);
        }

        /// <summary>Reads an image file; the type comes from the extension (.jpg/.jpeg/.png/.gif/.webp).</summary>
        public static ClaudeContent FromImageFile(string path) =>
            FromImage(File.ReadAllBytes(path), MediaTypeFromExtension(path));

        public static string MediaTypeFromExtension(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                var ext => throw new ArgumentException($"Unsupported image file \"{ext}\". Use JPEG, PNG, GIF or WebP.", nameof(path)),
            };
    }

    /// <summary>
    /// Structured output: Claude must answer by "calling" a tool whose input is the JSON we want.
    /// Forced tool use (tool_choice = this tool) makes the reply a single JSON object that
    /// follows <see cref="SchemaJson"/>.
    /// </summary>
    public sealed class JsonOutputSpec
    {
        public string ToolName { get; }
        public string Description { get; }
        public string SchemaJson { get; }

        public JsonOutputSpec(string toolName, string description, string schemaJson)
        {
            if (string.IsNullOrWhiteSpace(toolName)) throw new ArgumentException("Tool name is empty.", nameof(toolName));
            using (var doc = JsonDocument.Parse(schemaJson))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Schema must be a JSON object.", nameof(schemaJson));
            }
            ToolName = toolName;
            Description = description ?? "";
            SchemaJson = schemaJson;
        }
    }

    /// <summary>A single-turn request: optional system prompt, user content, optional JSON output.</summary>
    public sealed class ClaudeRequest
    {
        public string? System { get; init; }
        public IReadOnlyList<ClaudeContent> Content { get; init; } = Array.Empty<ClaudeContent>();
        public JsonOutputSpec? JsonOutput { get; init; }
        /// <summary>Overrides <see cref="ClaudeSettings.MaxTokens"/> when set.</summary>
        public int? MaxTokens { get; init; }
        public double? Temperature { get; init; }
    }

    public sealed class ClaudeResponse
    {
        public string Id { get; init; } = "";
        public string Model { get; init; } = "";
        /// <summary>end_turn, tool_use, max_tokens, ...</summary>
        public string StopReason { get; init; } = "";
        /// <summary>All text blocks joined.</summary>
        public string Text { get; init; } = "";
        /// <summary>The structured JSON (tool input) when a <see cref="JsonOutputSpec"/> was used and Claude answered with it.</summary>
        public string? Json { get; init; }
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public bool WasTruncated => StopReason == "max_tokens";
    }
}
