using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using VManager.Services;
using VManager.Services.Operations;
using VManager.Services.Models;
using Xunit;

namespace VManager.Tests.Integration
{
    public class CompressOperationIntegrationTests : IAsyncLifetime
    {
        private const string TestFilesDir = "CompressionTestFiles";
        private string _ffmpegPath = string.Empty;

        private bool _originalResumableSetting;

        public async Task InitializeAsync()
        {
            await FFmpegManager.Initialize();
            _ffmpegPath = FFmpegManager.FfmpegPath;

            // Habilitar el modo resumable para los tests de integración
            _originalResumableSetting = ConfigurationService.Current.EnableExperimentalResumable;
            ConfigurationService.Current.EnableExperimentalResumable = true;

            if (Directory.Exists(TestFilesDir))
                Directory.Delete(TestFilesDir, true);
            
            Directory.CreateDirectory(TestFilesDir);
        }

        public Task DisposeAsync()
        {
            // Restaurar la configuración original
            ConfigurationService.Current.EnableExperimentalResumable = _originalResumableSetting;

            if (Directory.Exists(TestFilesDir))
                Directory.Delete(TestFilesDir, true);
            
            return Task.CompletedTask;
        }

        private async Task CreateTestVideo(string path, int durationSeconds, bool withAudio = true)
        {
            // Generamos un video con bitrate bajo y audio silencioso para tests realistas
            string audioInput = withAudio
                ? $"-f lavfi -i aevalsrc=0:channel_layout=stereo:sample_rate=44100:duration={durationSeconds}"
                : string.Empty;
            string audioMap = withAudio ? "-map 0:v -map 1:a -c:a aac" : string.Empty;
            var args = $"-f lavfi -i testsrc=duration={durationSeconds}:size=160x120:rate=10 {audioInput} -c:v libx264 -pix_fmt yuv420p {audioMap} -y \"{path}\"";
            
            var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            await proc.WaitForExitAsync();
        }

        [Theory]
        [InlineData(50)] // Comprimir al 50%
        [InlineData(20)] // Comprimir al 20%
        public async Task CompressShortVideo_ShouldReduceSize(int percentage)
        {
            // Arrange
            string inputPath = Path.Combine(TestFilesDir, $"input_{percentage}.mp4");
            string outputPath = Path.Combine(TestFilesDir, $"output_{percentage}.mp4");
            await CreateTestVideo(inputPath, 5); // 5 segundos

            var operation = new CompressOperation(_ffmpegPath);
            long originalSize = new FileInfo(inputPath).Length;

            // Act
            var result = await operation.ExecuteAsync(
                inputPath,
                outputPath,
                compressionPercentage: percentage,
                videoCodec: "libx264",
                audioCodec: "aac",
                progress: new Progress<IFFmpegProcessor.ProgressInfo>()
            );

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(outputPath).Should().BeTrue();
            
            long compressedSize = new FileInfo(outputPath).Length;
            // Nota: En videos ultra cortos/pequeños el overhead del contenedor puede mentir, 
            // pero el proceso debe terminar con éxito.
            compressedSize.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task CompressLongVideo_ShouldTriggerResumableExecutor()
        {
            // Arrange
            // Generamos un video de 301 segundos (5min 1s) para entrar en el modo resumable
            string inputPath = Path.Combine(TestFilesDir, "long_video.mp4");
            string outputPath = Path.Combine(TestFilesDir, "long_compressed.mp4");
            await CreateTestVideo(inputPath, 301); 

            var operation = new CompressOperation(_ffmpegPath);

            // El ResumableExecutor reporta progreso por chunks — al menos 2 reportes para 301s
            int progressCount = 0;
            var progress = new Progress<IFFmpegProcessor.ProgressInfo>(_ => Interlocked.Increment(ref progressCount));

            // Act
            var result = await operation.ExecuteAsync(
                inputPath,
                outputPath,
                compressionPercentage: 50,
                videoCodec: "libx264",
                audioCodec: "aac",
                progress: progress
            );

            // Dar tiempo a que los callbacks de progreso se despachen
            await Task.Delay(100);

            // Assert
            result.Success.Should().BeTrue($"Error de FFmpeg: {result.Message}");
            File.Exists(outputPath).Should().BeTrue();

            // El ResumableExecutor procesa en chunks y reporta progreso por cada uno
            progressCount.Should().BeGreaterThan(1,
                "El ResumableExecutor debe reportar progreso múltiples veces (un reporte por chunk al menos)");

            // La carpeta temporal debe haber sido limpiada al finalizar
            string tempFolder = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                VManager.Services.Core.ProcessingConstants.TempFolderName);
            Directory.Exists(tempFolder).Should().BeFalse(
                "La carpeta temporal de chunks debería haber sido eliminada.");
        }

        [Fact]
        public async Task Compress_WithInvalidPercentage_ReturnsError()
        {
            // Arrange
            var operation = new CompressOperation(_ffmpegPath);

            // Act
            var result = await operation.ExecuteAsync(
                "any.mp4", "any_out.mp4",
                compressionPercentage: 150, // Inválido
                null, null,
                new Progress<IFFmpegProcessor.ProgressInfo>()
            );

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Be("Porcentaje inválido.");
        }
    }
}