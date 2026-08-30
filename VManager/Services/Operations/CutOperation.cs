// Services/Operations/CutOperation.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services;
using VManager.Services.Models;
using VManager.Services.Core.Execution;
using VManager.Services.Core.Media; // para FFmpegExecutor y ProcessingResult

namespace VManager.Services.Operations
{
    internal class CutOperation
    {
        private readonly IFFmpegExecutor _executor;
        private readonly IMediaAnalyzer _analyzer;

        public CutOperation(string ffmpegPath)
        {
            _executor = new FFmpegExecutor(ffmpegPath);
            _analyzer = new MediaAnalyzer();
        }

        public async Task<ProcessingResult> ExecuteAsync(
            string inputPath,
            string outputPath,
            TimeSpan start,
            TimeSpan duration,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken = default,
            PauseToken pauseToken = default)
        {
            inputPath = OutputPathBuilder.SanitizeFilename(inputPath);
            outputPath = OutputPathBuilder.SanitizeFilename(outputPath);
            
            var analysisResult = await _analyzer.AnalyzeAsync(inputPath);
            if (!analysisResult.Success)
                return new ProcessingResult(false, analysisResult.Message);

            var mediaInfo = analysisResult.Result!;
            double totalDuration = mediaInfo.Duration.TotalSeconds;

            string? warningMessage = null;
            if (start < TimeSpan.Zero || duration <= TimeSpan.Zero)
                return new ProcessingResult(false, "Parámetros de corte inválidos.");

            if (start.TotalSeconds + duration.TotalSeconds > totalDuration)
            {
                duration = TimeSpan.FromSeconds(totalDuration - start.TotalSeconds);
                warningMessage = $"Nota: La duración del corte se ajustó automáticamente a {duration:hh\\:mm\\:ss}.";
            }

            Console.WriteLine($"Corte - Video: copy, Audio: copy, Inicio: {start}, Duración: {duration:hh\\:mm\\:ss}");

            var args = FFMpegArguments
                .FromFileInput(inputPath, verifyExists: false, options => options.Seek(start))
                .OutputToFile(outputPath, overwrite: true, options =>
                {
                    options
                        .WithCustomArgument("-map 0")
                        .WithVideoCodec("copy")
                        .WithAudioCodec("copy")
                        .WithDuration(duration);
                });

            var result = await _executor.ExecuteAsync(
                inputPath,
                outputPath,
                args,
                duration.TotalSeconds,
                progress,
                cancellationToken,
                pauseToken
            );

            if (result.Success && !string.IsNullOrEmpty(warningMessage))
                return new ProcessingResult(true, result.Message + " " + warningMessage, result.OutputPath);

            return result;
        }
    }
}