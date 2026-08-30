// Services/Operations/AudioExtractOperation.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Core;
using VManager.Services.Models;
using VManager.Services.Core.Execution;
using VManager.Services.Core.Media;

namespace VManager.Services.Operations
{
    internal class AudioExtractOperation
    {
        private readonly IFFmpegExecutor _executor;
        private readonly IMediaAnalyzer _analyzer;

        // PARA TESTS
        public AudioExtractOperation(
            IFFmpegExecutor executor,
            IMediaAnalyzer analyzer)
        {
            _executor = executor;
            _analyzer = analyzer;
        }

        // PARA PRODUCCIÓN (FFmpegProcessor)
        public AudioExtractOperation(string ffmpegPath)
        {
            _executor = new FFmpegExecutor(ffmpegPath);
            _analyzer = new MediaAnalyzer();
        }

        public async Task<ProcessingResult> ExecuteAsync(
            string inputPath,
            string outputPath,
            string? videoCodec,
            string? audioCodec,
            string selectedAudioFormat,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken = default,
            PauseToken pauseToken = default)
        {
            inputPath = OutputPathBuilder.SanitizeFilename(inputPath);
            outputPath = OutputPathBuilder.SanitizeFilename(outputPath);
            
            Console.WriteLine($"[DEBUG] Audiofy - Origen: {inputPath}, Destino: {outputPath}, Formato: '{selectedAudioFormat}'");

            if (string.IsNullOrEmpty(selectedAudioFormat))
                return new ProcessingResult(false, "Formato de audio no especificado.");

            var analysisResult = await _analyzer.AnalyzeAsync(inputPath);
            if (!analysisResult.Success)
                return new ProcessingResult(false, analysisResult.Message);

            var mediaInfo = analysisResult.Result!;
            if (mediaInfo.AudioStreams.Count == 0)
                return new ProcessingResult(false, "El archivo no contiene pistas de audio.");

            var originalAudioStream = mediaInfo.AudioStreams.First();
            string originalCodec = originalAudioStream.CodecName?.ToLowerInvariant() ?? "unknown";
            double duration = mediaInfo.Duration.TotalSeconds;

            var decision = AudioCodecHelper.DecideAudioProcessing(originalCodec, selectedAudioFormat);

            Console.WriteLine($"[DEBUG] Decisión: {decision.Action}, Códec: {decision.Codec}, Razón: {decision.Reason}");

            // Formatos que NO soportan múltiples streams de audio
            var singleStreamFormats = new[] { "wav", "mp3", "flac", "aac", "wma" };
            bool isSingleStreamFormat = singleStreamFormats.Contains(selectedAudioFormat.ToLowerInvariant());
            
            // Si hay múltiples streams y el formato no los soporta, extraer por separado
            if (isSingleStreamFormat && mediaInfo.AudioStreams.Count > 1)
            {
                return await ExtractMultipleStreamsAsync(
                    inputPath, 
                    outputPath, 
                    mediaInfo, 
                    decision, 
                    duration, 
                    progress, 
                    cancellationToken,
                    pauseToken
                );
            }

            // Proceso normal para un solo stream o formatos que soportan múltiples
            var args = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, options =>
                {
                    options
                        .WithCustomArgument("-vn")
                        .WithCustomArgument(isSingleStreamFormat ? "-map 0:a:0" : "-map 0:a");

                    if (decision.Action == AudioProcessingAction.Copy)
                    {
                        options.WithAudioCodec("copy");
                    }
                    else
                    {
                        options
                            .WithAudioCodec(decision.Codec)
                            .WithAudioBitrate(decision.Bitrate);
                    }
                });

            var result = await _executor.ExecuteAsync(
                inputPath,
                outputPath,
                args,
                duration,
                progress,
                cancellationToken,
                pauseToken
            );

            if (result.Success)
            {
                string message = decision.Action == AudioProcessingAction.Copy
                    ? $"¡Audio extraído sin recodificar! ({decision.Reason})"
                    : "¡Audio extraído y convertido!";
                return new ProcessingResult(true, message, result.OutputPath);
            }

            return result;
        }

        private async Task<ProcessingResult> ExtractMultipleStreamsAsync(
            string inputPath,
            string outputPath,
            FFMpegCore.IMediaAnalysis mediaInfo,
            AudioProcessingDecision decision,  // <- Sin AudioCodecHelper.
            double duration,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken)
        {
            int streamCount = mediaInfo.AudioStreams.Count;
            string baseOutputPath = System.IO.Path.GetFileNameWithoutExtension(outputPath);
            string extension = System.IO.Path.GetExtension(outputPath);
            string directory = System.IO.Path.GetDirectoryName(outputPath) ?? "";

            for (int i = 0; i < streamCount; i++)
            {
                string streamOutputPath = System.IO.Path.Combine(
                    directory,
                    $"{baseOutputPath}_track{i + 1}{extension}"
                );

                var args = FFMpegArguments
                    .FromFileInput(inputPath)
                    .OutputToFile(streamOutputPath, overwrite: true, options =>
                    {
                        options
                            .WithCustomArgument("-vn")
                            .WithCustomArgument($"-map 0:a:{i}");

                        if (decision.Action == AudioProcessingAction.Copy)
                        {
                            options.WithAudioCodec("copy");
                        }
                        else
                        {
                            options
                                .WithAudioCodec(decision.Codec)
                                .WithAudioBitrate(decision.Bitrate);
                        }
                    });
                
                var result = await _executor.ExecuteAsync(
                    inputPath,
                    streamOutputPath,
                    args,
                    duration,
                    progress,
                    cancellationToken,
                    pauseToken
                );

                if (!result.Success)
                    return new ProcessingResult(false, $"Error extrayendo pista {i + 1}: {result.Message}");
            }

            return new ProcessingResult(
                true, 
                $"¡{streamCount} pistas de audio extraídas exitosamente!",
                outputPath
            );
        }
    }
}