using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CivilAI.Core.Brief;
using CivilAI.Core.Claude;

namespace CivilAI.Core.Style
{
    /// <summary>One reference image: a display name (file name) and its bytes.</summary>
    public sealed class StyleImage
    {
        public string Name { get; init; } = "";
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public string MediaType { get; init; } = "image/jpeg";
    }

    /// <summary>The Style panel's content: a prompt and/or up to six reference images.</summary>
    public sealed class StyleRequest
    {
        public string? Prompt { get; init; }
        public IReadOnlyList<StyleImage> Images { get; init; } = Array.Empty<StyleImage>();

        /// <summary>
        /// Builds a request from a brief's style; relative image paths are resolved against
        /// <paramref name="baseDirectory"/> (the brief's or drawing's folder).
        /// </summary>
        public static StyleRequest FromInput(StyleInput input, string baseDirectory)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            var images = input.Images.Select(path =>
            {
                var full = Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
                if (!File.Exists(full))
                    throw new FileNotFoundException($"Style image \"{path}\" was not found. Check the Style panel.", full);
                return new StyleImage
                {
                    Name = Path.GetFileName(path),
                    Data = File.ReadAllBytes(full),
                    MediaType = ClaudeContent.MediaTypeFromExtension(path),
                };
            }).ToList();
            return new StyleRequest { Prompt = input.Prompt, Images = images };
        }
    }

    public sealed class StyleReadResult
    {
        public StyleProfile Profile { get; init; } = new StyleProfile();

        /// <summary>True when the profile came from the cache and no Claude call was made.</summary>
        public bool FromCache { get; init; }

        /// <summary>Claude calls used (0 when cached, 1 = first answer was valid).</summary>
        public int Attempts { get; init; }

        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }

        /// <summary>Cache key of the inputs (see <see cref="StyleReader.CacheKey"/>).</summary>
        public string Key { get; init; } = "";
    }

    /// <summary>Claude could not produce a valid style profile within the allowed attempts.</summary>
    public sealed class StyleReadException : Exception
    {
        public IReadOnlyList<string> Problems { get; }
        public string? LastJson { get; }

        public StyleReadException(string message, IReadOnlyList<string> problems, string? lastJson) : base(message)
        {
            Problems = problems;
            LastJson = lastJson;
        }
    }

    /// <summary>
    /// Roadmap B.4: Style panel (images + prompt) → validated <see cref="StyleProfile"/> via Claude
    /// vision, with forced JSON output, a repair loop (as in 1.2) and an optional cache, so the same
    /// images and prompt are read once and reused across Generate runs.
    /// </summary>
    public sealed class StyleReader
    {
        public const string ToolName = "style_profile";
        public const string PromptVersion = "style-reader.v1";

        private readonly Func<ClaudeRequest, CancellationToken, Task<ClaudeResponse>> _send;
        private readonly StyleProfileCache? _cache;

        /// <summary>Total Claude calls allowed, including repairs. Default 2 (style is cheap to redo).</summary>
        public int MaxAttempts { get; init; } = 2;

        public StyleReader(ClaudeClient client, StyleProfileCache? cache = null)
            : this((client ?? throw new ArgumentNullException(nameof(client))).SendAsync, cache)
        {
        }

        /// <param name="send">Sends one request; replaced in tests.</param>
        public StyleReader(Func<ClaudeRequest, CancellationToken, Task<ClaudeResponse>> send, StyleProfileCache? cache = null)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _cache = cache;
        }

        public async Task<StyleReadResult> ReadAsync(StyleRequest request, CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Prompt) && request.Images.Count == 0)
                throw new ArgumentException("The Style panel is empty. Add reference images or a few words.", nameof(request));
            if (request.Images.Count > StyleInput.MaxImages)
                throw new ArgumentException($"Up to {StyleInput.MaxImages} reference images; got {request.Images.Count}.", nameof(request));
            if (MaxAttempts < 1) throw new InvalidOperationException("MaxAttempts must be at least 1.");

            var key = CacheKey(request);
            if (_cache is not null && _cache.TryGet(key, request.Images.Count, out var cached))
                return new StyleReadResult { Profile = cached!, FromCache = true, Attempts = 0, Key = key };

            var system = ReadSystemPrompt();
            var output = new JsonOutputSpec(ToolName,
                "Return the style profile read from the reference images and prompt.", StyleProfileJson.ReadSchema());
            var images = BuildImageContent(request);
            var text = BuildUserPrompt(request);

            string? lastJson = null;
            IReadOnlyList<string> problems = Array.Empty<string>();
            int inputTokens = 0, outputTokens = 0;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var content = new List<ClaudeContent>(images)
                {
                    ClaudeContent.FromText(attempt == 1 ? text : BuildRepairPrompt(text, lastJson, problems)),
                };
                var response = await _send(new ClaudeRequest
                {
                    System = system,
                    Content = content,
                    JsonOutput = output,
                }, ct).ConfigureAwait(false);
                inputTokens += response.InputTokens;
                outputTokens += response.OutputTokens;

                if (TryReadAnswer(response, request.Images.Count, out var profile, out lastJson, out problems))
                {
                    _cache?.Put(key, profile!);
                    return new StyleReadResult
                    {
                        Profile = profile!,
                        Attempts = attempt,
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        Key = key,
                    };
                }
            }

            throw new StyleReadException(
                $"Claude did not produce a valid style profile after {MaxAttempts} attempt(s). " +
                "Try other reference images or a clearer prompt, or check the problems listed.", problems, lastJson);
        }

        internal static bool TryReadAnswer(ClaudeResponse response, int imageCount,
            out StyleProfile? profile, out string? json, out IReadOnlyList<string> problems)
        {
            profile = null;
            json = response.Json;
            if (response.WasTruncated)
            {
                problems = new[] { "The answer was cut off (too long). Keep notes short and return the whole profile." };
                return false;
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                problems = new[] { $"No call to the {ToolName} tool. Answer only by calling it." };
                return false;
            }
            if (!StyleProfileJson.TryParse(json!, out var parsed, out var error))
            {
                problems = new[] { error ?? "The profile could not be read." };
                return false;
            }
            var issues = StyleProfileValidator.Validate(parsed!, imageCount);
            if (issues.Count > 0)
            {
                problems = issues.Select(i => i.ToString()).ToList();
                return false;
            }
            profile = parsed;
            problems = Array.Empty<string>();
            return true;
        }

        /// <summary>Each image is preceded by a short label so image notes can refer to it by number.</summary>
        public static IReadOnlyList<ClaudeContent> BuildImageContent(StyleRequest request)
        {
            var list = new List<ClaudeContent>();
            for (int i = 0; i < request.Images.Count; i++)
            {
                var img = request.Images[i];
                list.Add(ClaudeContent.FromText($"Reference image {i + 1}: {img.Name}"));
                list.Add(ClaudeContent.FromImage(img.Data, img.MediaType));
            }
            return list;
        }

        public static string BuildUserPrompt(StyleRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine(request.Images.Count == 0
                ? "No reference images were given."
                : $"{request.Images.Count} reference image(s) are above, in the designer's order.");
            sb.AppendLine(string.IsNullOrWhiteSpace(request.Prompt)
                ? "Style prompt: none given; read the style from the images."
                : "Style prompt from the landscape architect: " + request.Prompt!.Trim());
            sb.AppendLine("Return the style profile by calling the tool.");
            return sb.ToString();
        }

        public static string BuildRepairPrompt(string userPrompt, string? previousJson, IReadOnlyList<string> problems)
        {
            var sb = new StringBuilder(userPrompt);
            sb.AppendLine();
            sb.AppendLine("Your previous style profile could not be used:");
            foreach (var p in problems) sb.Append("- ").AppendLine(p);
            if (!string.IsNullOrEmpty(previousJson))
            {
                sb.AppendLine();
                sb.AppendLine("Previous profile:");
                sb.AppendLine(previousJson);
            }
            sb.AppendLine();
            sb.AppendLine("Fix every problem listed and return the complete corrected profile by calling the tool again.");
            return sb.ToString();
        }

        /// <summary>
        /// SHA-256 over the prompt version, the trimmed prompt and every image (order, type, bytes).
        /// Same inputs → same key; a new prompt version invalidates old entries.
        /// </summary>
        public static string CacheKey(StyleRequest request)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Text(string s)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                sha.AppendData(BitConverter.GetBytes(bytes.Length));
                sha.AppendData(bytes);
            }
            Text(PromptVersion);
            Text(request.Prompt?.Trim() ?? "");
            foreach (var img in request.Images)
            {
                Text(img.MediaType);
                sha.AppendData(BitConverter.GetBytes(img.Data.Length));
                sha.AppendData(img.Data);
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }

        public static string ReadSystemPrompt()
        {
            const string name = "CivilAI.Core.Style.Prompts." + PromptVersion + ".md";
            using var stream = typeof(StyleReader).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// File cache of style profiles: one <c>&lt;key&gt;.json</c> per input set in a folder
    /// (the plugin uses a folder next to the drawing or in the user's app data). Unreadable or
    /// invalid entries count as misses, so a stale file never breaks a run.
    /// </summary>
    public sealed class StyleProfileCache
    {
        public string Directory { get; }

        public StyleProfileCache(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Cache folder is empty.", nameof(directory));
            Directory = directory;
        }

        private string PathFor(string key) => System.IO.Path.Combine(Directory, key + ".json");

        public bool TryGet(string key, int imageCount, out StyleProfile? profile)
        {
            profile = null;
            var path = PathFor(key);
            if (!File.Exists(path)) return false;
            try
            {
                if (!StyleProfileJson.TryParse(File.ReadAllText(path), out var parsed, out _)) return false;
                if (StyleProfileValidator.Validate(parsed!, imageCount).Count > 0) return false;
                profile = parsed;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        public void Put(string key, StyleProfile profile)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathFor(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, StyleProfileJson.Serialize(profile));
            File.Move(temp, path, overwrite: true);
        }
    }
}
