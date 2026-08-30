using FluentAssertions;
using Moq;
using VManager.Services.Models;
using VManager.Services.Operations;
using VManager.Services.Core.Execution;
using VManager.Services.Core.Media;
using Xunit;
using FFMpegCore;
using VManager.Services;

namespace VManager.Tests.Unit
{
    public class CompressOperationTests
    {
        [Fact]
        public async Task ExecuteAsync_ShortVideo_UsesNormalExecutor()
        {
            // Crear archivo temporal de entrada
            var inputFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(inputFile, "dummy content");
            var outputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");

            try
            {
                // Mock de IMediaAnalysis con duración de 60s
                var mockMediaInfo = new Mock<IMediaAnalysis>();
                mockMediaInfo.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(60));

                // Mock de IMediaAnalyzer
                var mockAnalyzer = new Mock<IMediaAnalyzer>();
                mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                    .ReturnsAsync(new AnalysisResult<IMediaAnalysis>(true, "", mockMediaInfo.Object));

                // Mock de FFmpegExecutor
                var mockExecutor = new Mock<FFmpegExecutor>(FFmpegManager.FfmpegPath);
                mockExecutor.Setup(e => e.ExecuteAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<FFMpegArgumentProcessor>(),
                        It.IsAny<double>(),
                        It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                        It.IsAny<CancellationToken>()
                    ))
                    .ReturnsAsync(new ProcessingResult(true, "Mock ejecutado", outputFile));

                // Crear operación con mocks
                var operation = new CompressOperation(FFmpegManager.FfmpegPath, mockAnalyzer.Object);

                // Reemplazar executor por el mock
                typeof(CompressOperation)
                    .GetField("_executor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(operation, mockExecutor.Object);

                var result = await operation.ExecuteAsync(inputFile, outputFile, 50, null, null, null!);

                result.Success.Should().BeTrue();
                result.OutputPath.Should().Be(outputFile);

                mockExecutor.Verify(e => e.ExecuteAsync(
                    inputFile,
                    outputFile,
                    It.IsAny<FFMpegArgumentProcessor>(),
                    It.Is<double>(d => d == 60),
                    It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                    It.IsAny<CancellationToken>()
                ), Times.Once);
            }
            finally
            {
                File.Delete(inputFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
        }

        [Fact]
        public async Task ExecuteAsync_LongVideo_UsesResumableExecutor()
        {
            var originalResumable = ConfigurationService.Current.EnableExperimentalResumable;
            ConfigurationService.Current.EnableExperimentalResumable = true;

            // Archivo temporal
            var inputFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(inputFile, "dummy content");
            var outputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");

            try
            {
                var mockMediaInfo = new Mock<IMediaAnalysis>();
                mockMediaInfo.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(600));

                var mockAnalyzer = new Mock<IMediaAnalyzer>();
                mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                    .ReturnsAsync(new AnalysisResult<IMediaAnalysis>(true, "", mockMediaInfo.Object));

                var mockResumable = new Mock<ResumableFFmpegExecutor>(FFmpegManager.FfmpegPath);
                mockResumable.Setup(r => r.ExecuteResumableAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<Func<FFMpegArgumentOptions, FFMpegArgumentOptions>>(),
                        It.IsAny<double>(),
                        It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<PauseToken>(),
                        It.IsAny<string>()
                    ))
                    .ReturnsAsync(new ProcessingResult(true, "Mock resumable OK", outputFile));

                var operation = new CompressOperation(FFmpegManager.FfmpegPath, mockAnalyzer.Object);

                // Reemplazar el resumable executor por el mock
                typeof(CompressOperation)
                    .GetField("_resumableExecutor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(operation, mockResumable.Object);

                var result = await operation.ExecuteAsync(inputFile, outputFile, 50, null, null, null!);

                result.Success.Should().BeTrue();
                result.OutputPath.Should().Be(outputFile);

                mockResumable.Verify(r => r.ExecuteResumableAsync(
                    It.Is<string>(s => s == inputFile),
                    It.Is<string>(s => s == outputFile),
                    It.IsAny<Func<FFMpegCore.FFMpegArgumentOptions, FFMpegCore.FFMpegArgumentOptions>>(),
                    It.Is<double>(d => d == 600),
                    It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<PauseToken>(),
                    It.IsAny<string>()
                ), Times.Once);
            }
            finally
            {
                ConfigurationService.Current.EnableExperimentalResumable = originalResumable;
                File.Delete(inputFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
        }

        [Fact]
        public async Task ExecuteAsync_LongVideo_ForwardsPauseTokenToResumableExecutor()
        {
            var originalResumable = ConfigurationService.Current.EnableExperimentalResumable;
            ConfigurationService.Current.EnableExperimentalResumable = true;

            var inputFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(inputFile, "dummy content");
            var outputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
            var pauseSource = new PauseTokenSource();
            PauseToken capturedToken = default;

            try
            {
                var mockMediaInfo = new Mock<IMediaAnalysis>();
                mockMediaInfo.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(600));

                var mockAnalyzer = new Mock<IMediaAnalyzer>();
                mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                    .ReturnsAsync(new AnalysisResult<IMediaAnalysis>(true, "", mockMediaInfo.Object));

                var mockResumable = new Mock<ResumableFFmpegExecutor>(FFmpegManager.FfmpegPath);
                mockResumable.Setup(r => r.ExecuteResumableAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<Func<FFMpegArgumentOptions, FFMpegArgumentOptions>>(),
                        It.IsAny<double>(),
                        It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<PauseToken>(),
                        It.IsAny<string>()
                    ))
                    .Callback<string, string, Func<FFMpegArgumentOptions, FFMpegArgumentOptions>, double,
                        IProgress<IFFmpegProcessor.ProgressInfo>, CancellationToken, PauseToken, string?>(
                        (_, _, _, _, _, _, pt, _) => capturedToken = pt)
                    .ReturnsAsync(new ProcessingResult(true, "Mock resumable OK", outputFile));

                var operation = new CompressOperation(FFmpegManager.FfmpegPath, mockAnalyzer.Object);
                typeof(CompressOperation)
                    .GetField("_resumableExecutor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(operation, mockResumable.Object);

                await operation.ExecuteAsync(inputFile, outputFile, 50, null, null, null!, pauseToken: pauseSource.Token);

                capturedToken.IsPaused.Should().BeFalse();
                pauseSource.Pause();
                capturedToken.IsPaused.Should().BeTrue();
            }
            finally
            {
                ConfigurationService.Current.EnableExperimentalResumable = originalResumable;
                File.Delete(inputFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public async Task ExecuteAsync_InvalidCompression_ReturnsError(int compression)
        {
            var mockAnalyzer = new Mock<IMediaAnalyzer>();
            var tempInput = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempInput, "dummy");
                var operation = new CompressOperation(FFmpegManager.FfmpegPath, mockAnalyzer.Object);

                var result = await operation.ExecuteAsync(tempInput, "out.mp4", compression, null, null, null!);

                result.Success.Should().BeFalse();
                result.Message.Should().Contain("Porcentaje inválido");
            }
            finally
            {
                File.Delete(tempInput);
            }
        }
    }
}
