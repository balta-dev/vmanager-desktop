using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using VManager.Services;
using VManager.Services.Operations;
using VManager.Services.Models;
using FFMpegCore;
using Xunit;

namespace VManager.Tests.Integration
{
    public class ConvertOperationIntegrationTests : IAsyncLifetime
    {
        private const string TestFilesDir = "IntegrationTestFiles";
        private string _ffmpegPath = string.Empty;
        private bool _originalResumableSetting;

        public async Task InitializeAsync()
        {
            await FFmpegManager.Initialize();
            _ffmpegPath = FFmpegManager.FfmpegPath;

            // Habilitar el modo resumable para los tests de integración
            _originalResumableSetting = ConfigurationService.Current.EnableExperimentalResumable;
            ConfigurationService.Current.EnableExperimentalResumable = true;

            // Limpieza inicial
            if (Directory.Exists(TestFilesDir))
                Directory.Delete(TestFilesDir, true);
            
            Directory.CreateDirectory(TestFilesDir);
        }

        public Task DisposeAsync()
        {
            // Restaurar la configuración original
            ConfigurationService.Current.EnableExperimentalResumable = _originalResumableSetting;

            // Limpieza al finalizar (opcional)
            // if (Directory.Exists(TestFilesDir)) Directory.Delete(TestFilesDir, true);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ConvertShortVideo_ProducesOutputFile()
        {
            string inputPath = Path.Combine(TestFilesDir, "test_input.mp4");
            string outputPath = Path.Combine(TestFilesDir, "test_output.mp4");

            // Generar el video de entrada CON audio silencioso para que sea realista
            var generateArgs = $"-f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i aevalsrc=0:channel_layout=stereo:duration=2 -map 0:v -map 1:a -c:v libx264 -pix_fmt yuv420p -c:a aac -y \"{inputPath}\"";
    
            var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = generateArgs,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
    
            proc.Start();
            await proc.WaitForExitAsync();
    
            if (proc.ExitCode != 0) 
                throw new Exception($"Fallo al crear video de prueba: {await proc.StandardError.ReadToEndAsync()}");

            // Ejecutar la operación de conversión
            var operation = new ConvertOperation(_ffmpegPath);
            var result = await operation.ExecuteAsync(
                inputPath,
                outputPath,
                videoCodec: "libx264",
                audioCodec: "aac",
                selectedFormat: "mp4",
                progress: new Progress<IFFmpegProcessor.ProgressInfo>(),
                cancellationToken: CancellationToken.None
            );

            // Validar
            result.Success.Should().BeTrue($"Error de FFmpeg: {result.Message}");
            File.Exists(outputPath).Should().BeTrue();
        }
        
        [Fact]
        public async Task ConvertLongVideo_ShouldTriggerResumableExecutor()
        {
            string inputPath = Path.Combine(TestFilesDir, "long_convert_input.mp4");
            string outputPath = Path.Combine(TestFilesDir, "long_convert_output.mkv");

            // Generar video de 301 segundos (5min 1s) para entrar en modo resumable (umbral: > 300s)
            var generateArgs = $"-f lavfi -i testsrc=duration=301:size=160x120:rate=10 -f lavfi -i aevalsrc=0:channel_layout=stereo:duration=301 -map 0:v -map 1:a -c:v libx264 -pix_fmt yuv420p -c:a aac -y \"{inputPath}\"";
            
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = generateArgs,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            await proc!.WaitForExitAsync();

            // Ejecutar Conversión
            var operation = new ConvertOperation(_ffmpegPath);
            
            // El ResumableExecutor reporta progreso por chunks — al menos 2 reportes para 301s
            int progressCount = 0;
            var progress = new Progress<IFFmpegProcessor.ProgressInfo>(_ => Interlocked.Increment(ref progressCount));

            var result = await operation.ExecuteAsync(
                inputPath,
                outputPath,
                videoCodec: "libx265", // distinto del h264 de entrada → fuerza recodificación y modo resumable
                audioCodec: "aac",
                selectedFormat: "mkv",
                progress: progress,
                cancellationToken: CancellationToken.None
            );

            // Dar tiempo a que los callbacks de progreso se despachen
            await Task.Delay(100);

            // Validaciones
            result.Success.Should().BeTrue($"La conversión resumible falló: {result.Message}");
            File.Exists(outputPath).Should().BeTrue();
            
            // CRÍTICO: Verificar que se usó el ResumableExecutor (múltiples reportes de progreso)
            progressCount.Should().BeGreaterThan(1, "El ResumableExecutor debe reportar progreso para cada chunk procesado");
            
            // Verificar que el archivo final realmente sea el formato pedido (MKV)
            var mediaInfo = await FFProbe.AnalyseAsync(outputPath);
            mediaInfo.Format.FormatName.Should().Contain("matroska");

            // Verificar limpieza de temporales con el path correcto
            string tempFolder = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                VManager.Services.Core.ProcessingConstants.TempFolderName);
            Directory.Exists(tempFolder).Should().BeFalse("La carpeta temporal de chunks debería haber sido eliminada.");
        }
    }
}