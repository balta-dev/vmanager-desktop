// Services/Operations/CompressOperation.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services;
using VManager.Services.Models;
using VManager.Services.Core.Execution;
using VManager.Services.Core.Media;

namespace VManager.Services.Operations
{
    internal class CompressOperation
    {
        private readonly IFFmpegExecutor _executor;
        private readonly IResumableFFmpegExecutor _resumableExecutor;
        private readonly IMediaAnalyzer _analyzer;
        
        // Constructor principal (para tests unitarios)
        public CompressOperation(
            IFFmpegExecutor executor,
            IResumableFFmpegExecutor resumableExecutor,
            IMediaAnalyzer analyzer)
        {
            _executor = executor;
            _resumableExecutor = resumableExecutor;
            _analyzer = analyzer;
        }

        // Constructor de conveniencia (para producción)
        public CompressOperation(string ffmpegPath, IMediaAnalyzer? analyzer = null)
        {
            _executor = new FFmpegExecutor(ffmpegPath);
            _resumableExecutor = new ResumableFFmpegExecutor(ffmpegPath);
            _analyzer = analyzer ?? new MediaAnalyzer();
        }

        public virtual async Task<ProcessingResult> ExecuteAsync(
            string inputPath,
            string outputPath,
            int compressionPercentage,
            string? videoCodec,
            string? audioCodec,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken = default,
            PauseToken pauseToken = default)
        {
            inputPath = OutputPathBuilder.SanitizeFilename(inputPath);
            outputPath = OutputPathBuilder.SanitizeFilename(outputPath);
            
            if (compressionPercentage <= 0 || compressionPercentage > 100)
                return new ProcessingResult(false, "Porcentaje inválido.");

            var (selectedVideoCodec, selectedAudioCodec) = (videoCodec ?? "libx264", audioCodec ?? "aac");

            var analysisResult = await _analyzer.AnalyzeAsync(inputPath);
            if (!analysisResult.Success)
                return new ProcessingResult(false, analysisResult.Message);

            var mediaInfo = analysisResult.Result!;
            double duration = mediaInfo.Duration.TotalSeconds;

            long sizeBytes = new FileInfo(inputPath).Length;
            long targetSize = sizeBytes * compressionPercentage / 100;
            int targetBitrate = (int)((targetSize * 8) / duration / 1000);
            targetBitrate = Math.Max(targetBitrate, 100);

            Console.WriteLine($"Compresión - Video: {selectedVideoCodec}, Audio: {selectedAudioCodec}, Bitrate: {targetBitrate} kbps");

            bool useResumable = ConfigurationService.Current.EnableExperimentalResumable && duration > 300;
            
            if (useResumable)
            {
                return await _resumableExecutor.ExecuteResumableAsync(
                       inputPath,
                       outputPath,
                       options =>
                       {
                           options
                               .WithCustomArgument("-map 0")
                               .WithVideoCodec(selectedVideoCodec)
                               .WithVideoBitrate(targetBitrate);

                           if (mediaInfo.PrimaryAudioStream != null)
                           {
                               options
                                   .WithAudioCodec(selectedAudioCodec)
                                   .WithAudioBitrate(128);
                           }

                           HardwareAccelerationConfigurator.Configure(options, selectedVideoCodec);
                           return options;
                       },
                       duration,
                       progress,
                       cancellationToken,
                       pauseToken
                );
            }

            // Modo normal
            var args = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, options =>
                {
                    options.WithCustomArgument("-map 0:v");
                    if (mediaInfo.PrimaryAudioStream != null)
                    {
                        options.WithCustomArgument("-map 0:a");
                    }

                    options
                        .WithVideoCodec(selectedVideoCodec)
                        .WithVideoBitrate(targetBitrate);

                    if (mediaInfo.PrimaryAudioStream != null)
                    {
                        options
                            .WithAudioCodec(selectedAudioCodec)
                            .WithAudioBitrate(128);
                    }

                    HardwareAccelerationConfigurator.Configure(options, selectedVideoCodec);
                });

            return await _executor.ExecuteAsync(inputPath, outputPath, args, duration, progress, cancellationToken, pauseToken);
        }
    }
}