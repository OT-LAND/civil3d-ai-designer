using System;
using System.IO;
using System.Linq;
using CivilAI.Core.Claude;
using CivilAI.Core.Diagnostics;
using Xunit;

namespace CivilAI.Core.Tests.Diagnostics
{
    public class HelloReportTests
    {
        private const string Path = @"C:\Users\x\AppData\Roaming\CivilAI\settings.json";

        [Fact]
        public void Ready_session_reports_versions_civil3d_and_claude_without_key()
        {
            var lines = HelloReport.Build("0.3.0", "26.0", true,
                () => new ClaudeSettings { ApiKey = "sk-ant-secret-123", Model = "claude-x" }, Path);

            Assert.Equal("Civil 3D AI Landscape Designer is loaded.", lines[0]);
            Assert.Contains("Plugin 0.3.0", lines[1]);
            Assert.Contains("host 26.0", lines[1]);
            Assert.Contains("Core ", lines[1]);
            Assert.Equal("Civil 3D: ready.", lines[2]);
            Assert.Equal("Claude: ready (model claude-x, API key set).", lines[3]);
            Assert.DoesNotContain(lines, l => l.Contains("sk-ant-secret-123"));
        }

        [Fact]
        public void Missing_key_points_to_settings_file_and_env_variable()
        {
            var lines = HelloReport.Build("0.3.0", "26.0", true, () => new ClaudeSettings(), Path);

            var claude = lines.Last();
            Assert.StartsWith("Claude: no API key yet.", claude);
            Assert.Contains(Path, claude);
            Assert.Contains(ClaudeSettings.ApiKeyVariable, claude);
        }

        [Fact]
        public void Plain_autocad_is_reported_not_thrown()
        {
            var lines = HelloReport.Build("0.3.0", "26.0", false, () => new ClaudeSettings(), Path);
            Assert.StartsWith("Civil 3D: not found", lines[2]);
        }

        [Theory]
        [InlineData("format")]
        [InlineData("io")]
        public void Broken_settings_file_becomes_a_readable_line(string kind)
        {
            Func<ClaudeSettings> load = kind == "format"
                ? () => throw new FormatException("maxTokens must be positive.")
                : () => throw new IOException("file is locked");

            var lines = HelloReport.Build("0.3.0", "26.0", true, load, Path);

            Assert.StartsWith("Claude: settings file could not be read", lines.Last());
            Assert.Contains(Path, lines.Last());
        }

        [Fact]
        public void Broken_json_from_real_loader_is_reported()
        {
            var file = System.IO.Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "{ not json");
                var lines = HelloReport.Build("0.3.0", "26.0", true,
                    () => ClaudeSettings.Load(_ => null, file), file);
                Assert.StartsWith("Claude: settings file could not be read", lines.Last());
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void Blank_versions_show_unknown()
        {
            var lines = HelloReport.Build(" ", "", true, () => new ClaudeSettings(), Path);
            Assert.Contains("Plugin unknown", lines[1]);
            Assert.Contains("host unknown", lines[1]);
        }

        [Fact]
        public void Null_loader_is_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => HelloReport.Build("1", "1", true, null!, Path));
        }
    }
}
