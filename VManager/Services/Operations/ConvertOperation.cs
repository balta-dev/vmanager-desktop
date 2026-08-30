// Services/Operations/ConvertOperation.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Core;
using VManager.Services.Models;
using VManager.Services.Core.Execution;
using VManager.Services.Core.Media;

namespace VManager.Services.Operations
{
    internal class ConvertOperation
    {
        private readonly IFFmpegExecutor _executor;
        private readonly IResumableFFmpegExecutor _resumableExecutor;
        private readonly IMediaAnalyzer _analyzer;

        public ConvertOperation(string ffmpegPath)
        {
            _executor = new FFmpegExecutor(ffmpegPath);
            _resumableExecutor = new ResumableFFmpegExecutor(ffmpegPath);
            _analyzer = new MediaAnalyzer();
        }
        
        // PARA TESTS
        public ConvertOperation(
            IFFmpegExecutor executor,
            IResumableFFmpegExecutor resumableExecutor,
            IMediaAnalyzer analyzer)
        {
            _executor = executor;
            _resumableExecutor = resumableExecutor;
            _analyzer = analyzer;
        }

        public async Task<ProcessingResult> ExecuteAsync(
            string inputPath,
            string outputPath,
            string? videoCodec,
            string? audioCodec,
            string selectedFormat,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken = default,
            PauseToken pauseToken = default)
        {
            inputPath = OutputPathBuilder.SanitizeFilename(inputPath);
            outputPath = OutputPathBuilder.SanitizeFilename(outputPath);
            
            // Defaults de códecs
            string selectedVideoCodec = videoCodec ?? "libx264";
            string selectedAudioCodec = audioCodec ?? "aac";

            // Caso especial: MOV sin códec especificado → DNxHR
            if (selectedFormat.Equals("mov", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(videoCodec))
            {
                selectedVideoCodec = "dnxhd";
                selectedAudioCodec = "pcm_s24le";
            }

            // Análisis del video
            var analysisResult = await _analyzer.AnalyzeAsync(inputPath);
            if (!analysisResult.Success)
                return new ProcessingResult(false, analysisResult.Message);

            var mediaInfo = analysisResult.Result!;
            double duration = mediaInfo.Duration.TotalSeconds;

            // Detectar si necesitamos recodificar o solo cambiar contenedor
            bool needsReencoding = RequiresReencoding(mediaInfo, selectedVideoCodec, selectedAudioCodec);

            Console.WriteLine($"Conversión - Video: {selectedVideoCodec}, Audio: {selectedAudioCodec}, Formato: {selectedFormat}");
            Console.WriteLine($"[DEBUG] Recodificación necesaria: {needsReencoding}");
            bool useResumable = ConfigurationService.Current.EnableExperimentalResumable && needsReencoding && duration > 300;
            
            if (useResumable)
            {
                return await _resumableExecutor.ExecuteResumableAsync(
                    inputPath,
                    outputPath,
                    options =>
                    {
                        options
                            .WithCustomArgument("-map 0")
                            .WithVideoCodec(selectedVideoCodec);

                        if (mediaInfo.PrimaryAudioStream != null)
                        {
                            options.WithAudioCodec(selectedAudioCodec);
                        }

                        ApplySpecialCodecOptions(options, selectedVideoCodec, mediaInfo.PrimaryAudioStream != null);

                        return options;
                    },
                    duration,
                    progress,
                    cancellationToken,
                    pauseToken
                );
            }

            // Modo normal (rápido)
            var args = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, options =>
                {
                    options.WithCustomArgument("-map 0:v");
                    if (mediaInfo.PrimaryAudioStream != null)
                    {
                        options.WithCustomArgument("-map 0:a");
                    }
                    
                    if (needsReencoding)
                    {
                        options.WithVideoCodec(selectedVideoCodec);
                        if (mediaInfo.PrimaryAudioStream != null)
                        {
                            options.WithAudioCodec(selectedAudioCodec);
                        }

                        ApplySpecialCodecOptions(options, selectedVideoCodec, mediaInfo.PrimaryAudioStream != null);
                    }
                    else
                    {
                        // Solo cambiar contenedor, sin recodificar (ULTRA RÁPIDO)
                        options.WithCustomArgument("-c copy");
                    }
                });

            return await _executor.ExecuteAsync(
                inputPath,
                outputPath,
                args,
                duration,
                progress,
                cancellationToken,
                pauseToken
            );
        }

        // Detecta si necesitamos recodificar o solo cambiar contenedor
        private bool RequiresReencoding(IMediaAnalysis mediaInfo, string targetVideoCodec, string targetAudioCodec)
        {
            var videoStream = mediaInfo.PrimaryVideoStream;
            var audioStream = mediaInfo.PrimaryAudioStream;

            if (videoStream == null)
                return true; // Sin video, recodificar por seguridad

            // Normalizar nombres de códecs
            string currentVideoCodec = videoStream.CodecName?.ToLower() ?? "";
            string currentAudioCodec = audioStream?.CodecName?.ToLower() ?? "";
            string targetVideo = targetVideoCodec.ToLower();
            string targetAudio = targetAudioCodec.ToLower();

            // Mapeo de códecs equivalentes
            bool videoMatches = 
                (currentVideoCodec == targetVideo) ||
                (currentVideoCodec == "h264" && targetVideo == "libx264") ||
                (currentVideoCodec == "hevc" && targetVideo == "libx265") ||
                (currentVideoCodec == "vp9" && targetVideo == "libvpx-vp9");

            bool audioMatches = audioStream == null || 
                (currentAudioCodec == targetAudio) ||
                (currentAudioCodec == "aac" && targetAudio == "aac");

            // Si ambos coinciden, solo necesitamos cambiar el contenedor
            return !(videoMatches && audioMatches);
        }

        // Método privado para evitar duplicación de lógica DNxHR
        private static void ApplySpecialCodecOptions(FFMpegArgumentOptions options, string videoCodec, bool hasAudio)
        {
            if (HardwareAccelerationConfigurator.IsDNxHRCodec(videoCodec))
            {
                options
                    .WithCustomArgument("-profile:v dnxhr_hq")
                    .WithCustomArgument("-pix_fmt yuv422p");
            }
            else
            {
                if (hasAudio)
                {
                    options.WithAudioBitrate(128);
                }
                HardwareAccelerationConfigurator.Configure(options, videoCodec);
            }
        }
    }
}