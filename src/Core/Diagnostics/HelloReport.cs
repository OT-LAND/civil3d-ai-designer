using System;
using System.Collections.Generic;
using CivilAI.Core.Claude;

namespace CivilAI.Core.Diagnostics
{
    /// <summary>
    /// Text for the AIHELLO command and the palette's status panel (roadmap 0.3): proves the
    /// plugin loaded and tells the designer, in plain words, whether it is ready to talk to Claude.
    /// Pure text building so it is unit-tested here; the plugin only gathers the inputs.
    /// </summary>
    public static class HelloReport
    {
        public const string ProductName = "Civil 3D AI Landscape Designer";

        /// <summary>Version of this Core assembly (major.minor.build).</summary>
        public static string CoreVersion =>
            typeof(HelloReport).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>
        /// Builds the report lines.
        /// </summary>
        /// <param name="pluginVersion">Plugin assembly version.</param>
        /// <param name="hostVersion">AutoCAD/Civil 3D version string, e.g. "26.0".</param>
        /// <param name="civil3DReady">True when a Civil 3D document is available (not plain AutoCAD).</param>
        /// <param name="loadSettings">Loads Claude settings; errors become a readable line, never an exception.</param>
        /// <param name="settingsPath">Shown when the key is missing or the file is broken, so the designer knows where to fix it.</param>
        public static IReadOnlyList<string> Build(
            string pluginVersion,
            string hostVersion,
            bool civil3DReady,
            Func<ClaudeSettings> loadSettings,
            string settingsPath)
        {
            if (loadSettings is null) throw new ArgumentNullException(nameof(loadSettings));

            var lines = new List<string>
            {
                $"{ProductName} is loaded.",
                $"Plugin {Clean(pluginVersion)} · Core {CoreVersion} · host {Clean(hostVersion)}",
                civil3DReady
                    ? "Civil 3D: ready."
                    : "Civil 3D: not found in this drawing session (plain AutoCAD?). Grading tools will need Civil 3D.",
            };

            try
            {
                var settings = loadSettings();
                lines.Add(settings.HasApiKey
                    ? $"Claude: ready (model {settings.Model}, API key set)."
                    : $"Claude: no API key yet. Add \"anthropicApiKey\" to {settingsPath} or set {ClaudeSettings.ApiKeyVariable}.");
            }
            catch (Exception ex) when (ex is FormatException || ex is System.Text.Json.JsonException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                lines.Add($"Claude: settings file could not be read ({settingsPath}): {ex.Message}");
            }

            return lines;
        }

        private static string Clean(string? s) => string.IsNullOrWhiteSpace(s) ? "unknown" : s.Trim();
    }
}
