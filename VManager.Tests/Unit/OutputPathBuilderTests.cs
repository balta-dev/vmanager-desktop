using Xunit;
using FluentAssertions;
using VManager.Services.Core.Media;

namespace VManager.Tests.Unit
{
    public class OutputPathBuilderTests
    {
        private static string SampleInputPath =>
            Path.Combine("Videos", "test.mp4");

        // ─────────────────────────────────────────────
        // COMPRESS
        // ─────────────────────────────────────────────

        [Fact]
        public void GetCompressOutputPath_AddsPercentageSuffix()
        {
            var result = OutputPathBuilder.GetCompressOutputPath(SampleInputPath, 75);

            result.Should().Be(Path.Combine("Videos", "test-75.mp4"));
        }

        [Fact]
        public void GetCompressOutputPath_AllowsAnyPercentage()
        {
            var result = OutputPathBuilder.GetCompressOutputPath(SampleInputPath, -10);

            result.Should().Be(Path.Combine("Videos", "test--10.mp4"));
        }

        // ─────────────────────────────────────────────
        // CONVERT
        // ─────────────────────────────────────────────

        [Fact]
        public void GetConvertOutputPath_AddsConvertSuffixAndFormat()
        {
            var inputPath = Path.Combine("Videos", "test.mkv");
            var result = OutputPathBuilder.GetConvertOutputPath(inputPath, "webm");

            result.Should().Be(Path.Combine("Videos", "test-VCONV.webm"));
        }

        [Fact]
        public void GetConvertOutputPath_FileWithMultipleDots_PreservesName()
        {
            var inputPath = Path.Combine("Videos", "my.video.final.mkv");
            var result = OutputPathBuilder.GetConvertOutputPath(inputPath, "mp4");

            result.Should().Be(Path.Combine("Videos", "my.video.final-VCONV.mp4"));
        }

        // ─────────────────────────────────────────────
        // AUDIO
        // ─────────────────────────────────────────────

        [Theory]
        [InlineData("mp3")]
        [InlineData(".mp3")]
        public void GetAudioOutputPath_NormalizesAudioFormat(string format)
        {
            var inputPath = Path.Combine("Videos", "test.mkv");
            var result = OutputPathBuilder.GetAudioOutputPath(inputPath, format);

            result.Should().Be(Path.Combine("Videos", "test-ACONV.mp3"));
        }

        // ─────────────────────────────────────────────
        // CUT
        // ─────────────────────────────────────────────

        [Fact]
        public void GetCutOutputPath_AppendsCutSuffixWithTimestamps()
        {
            var start = TimeSpan.Zero;
            var duration = TimeSpan.Zero;
            var result = OutputPathBuilder.GetCutOutputPath(SampleInputPath, start, duration);

            result.Should().Be(Path.Combine("Videos", "test-VCUT_00-00-00_00-00-00.mp4"));
        }

        // ─────────────────────────────────────────────
        // TEMP
        // ─────────────────────────────────────────────

        [Fact]
        public void GetTempDirectory_UsesConfiguredTempFolderName()
        {
            var inputPath = Path.Combine("Videos", "test.mp4");

            var result = OutputPathBuilder.GetTempDirectory(inputPath);

            result.Should().Be(
                Path.Combine("Videos", "vmanager_temp"));
        }
    }
}
