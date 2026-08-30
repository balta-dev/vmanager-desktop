using FluentAssertions;
using Moq;
using VManager.Services.Models;
using VManager.Services.Operations;
using VManager.Services.Core.Execution;
using VManager.Services.Core.Media;
using Xunit;
using FFMpegCore;
using VManager.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VManager.Tests.Unit
{
    public class ConvertOperationTests : IAsyncLifetime
    {
        private string _ffmpegPath = string.Empty;

        public async Task InitializeAsync()
        {
            await FFmpegManager.Initialize();
            _ffmpegPath = FFmpegManager.FfmpegPath;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ExecuteAsync_ShortVideo_UsesNormalExecutor()
        {
            // Arrange
            var inputFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(inputFile, "dummy content");
            var outputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");

            try
            {
                // Mock de IMediaAnalysis con duración de 60s
                var mockMediaInfo = new Mock<IMediaAnalysis>();
                mockMediaInfo.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(60));
                mockMediaInfo.Setup(m => m.PrimaryVideoStream).Returns(new VideoStream { CodecName = "h264" });
                mockMediaInfo.Setup(m => m.PrimaryAudioStream).Returns(new AudioStream { CodecName = "aac" });

                // Mock de IMediaAnalyzer
                var mockAnalyzer = new Mock<IMediaAnalyzer>();
                mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                    .ReturnsAsync(new AnalysisResult<IMediaAnalysis>(true, "", mockMediaInfo.Object));

                // Mock de FFmpegExecutor - IMPORTANTE: CallBase = false
                var mockExecutor = new Mock<FFmpegExecutor>(_ffmpegPath) { CallBase = false };
                mockExecutor.Setup(e => e.ExecuteAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<FFMpegArgumentProcessor>(),
                        It.IsAny<double>(),
                        It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                        It.IsAny<CancellationToken>()
                    ))
                    .ReturnsAsync(new ProcessingResult(true, "Mock ejecutado", outputFile));

                var mockResumable = new Mock<ResumableFFmpegExecutor>(_ffmpegPath) { CallBase = false };

                // Crear operación inyectando los mocks directamente
                var operation = new ConvertOperation(mockExecutor.Object, mockResumable.Object, mockAnalyzer.Object);

                // Act
                var result = await operation.ExecuteAsync(inputFile, outputFile, "libx264", "aac", "mp4", null!, CancellationToken.None);

                // Assert
                result.Success.Should().BeTrue();
                result.OutputPath.Should().Be(outputFile);

                // CRÍTICO: Verificar que se usó el executor NORMAL
                mockExecutor.Verify(e => e.ExecuteAsync(
                    inputFile,
                    outputFile,
                    It.IsAny<FFMpegArgumentProcessor>(),
                    It.Is<double>(d => d == 60),
                    It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                    It.IsAny<CancellationToken>()
                ), Times.Once, "Para videos cortos (<300s) se debe usar el FFmpegExecutor normal");
            }
            finally
            {
                if (File.Exists(inputFile)) File.Delete(inputFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
        }

        [Fact]
        public async Task ExecuteAsync_LongVideo_UsesResumableExecutor()
        {
            var originalResumable = ConfigurationService.Current.EnableExperimentalResumable;
            ConfigurationService.Current.EnableExperimentalResumable = true;

            // Arrange
            var inputFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(inputFile, "dummy content");
            var outputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");

            try
            {
                var mockMediaInfo = new Mock<IMediaAnalysis>();
                mockMediaInfo.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(600));
                // IMPORTANTE: Configurar códecs DIFERENTES para forzar needsReencoding = true
                mockMediaInfo.Setup(m => m.PrimaryVideoStream).Returns(new VideoStream { CodecName = "vp9" }); // Diferente a libx264
                mockMediaInfo.Setup(m => m.PrimaryAudioStream).Returns(new AudioStream { CodecName = "opus" }); // Diferente a aac

                var mockAnalyzer = new Mock<IMediaAnalyzer>();
                mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                    .ReturnsAsync(new AnalysisResult<IMediaAnalysis>(true, "", mockMediaInfo.Object));

                var mockExecutor = new Mock<FFmpegExecutor>(_ffmpegPath) { CallBase = false };

                // IMPORTANTE: CallBase = false para evitar ejecución real
                var mockResumable = new Mock<ResumableFFmpegExecutor>(_ffmpegPath) { CallBase = false };
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

                // Crear operación inyectando los mocks directamente
                var operation = new ConvertOperation(mockExecutor.Object, mockResumable.Object, mockAnalyzer.Object);

                // Act
                var result = await operation.ExecuteAsync(inputFile, outputFile, "libx264", "aac", "mp4", null!, CancellationToken.None);

                // Assert
                result.Should().NotBeNull("ExecuteAsync should return ProcessingResult, not null");
                result.Success.Should().BeTrue();
                result.OutputPath.Should().Be(outputFile);

                // CRÍTICO: Verificar que se usó el ResumableExecutor
                mockResumable.Verify(r => r.ExecuteResumableAsync(
                    It.Is<string>(s => s == inputFile),
                    It.Is<string>(s => s == outputFile),
                    It.IsAny<Func<FFMpegCore.FFMpegArgumentOptions, FFMpegCore.FFMpegArgumentOptions>>(),
                    It.Is<double>(d => d == 600),
                    It.IsAny<IProgress<IFFmpegProcessor.ProgressInfo>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<PauseToken>(),
                    It.IsAny<string>()
                ), Times.Once, "Para videos largos (>=300s) que requieren recodificación se debe usar el ResumableExecutor");
            }
            finally
            {
                ConfigurationService.Current.EnableExperimentalResumable = originalResumable;
                if (File.Exists(inputFile)) File.Delete(inputFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
        }
    }
}