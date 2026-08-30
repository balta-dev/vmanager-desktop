using System;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Models;
using VManager.Services.Core.Execution;

namespace VManager.Services
{
    public interface IFFmpegProcessor
    {
        public record ProgressInfo(double Progress, TimeSpan Remaining);

        Task<AnalysisResult<IMediaAnalysis>> AnalyzeVideoAsync(string inputPath);

        Task<ProcessingResult> CutAsync(
            string inputPath, 
            string outputPath, 
            TimeSpan start, 
            TimeSpan duration,
            IProgress<ProgressInfo> progress, 
            CancellationToken ct = default,
            PauseToken pauseToken = default);

        Task<ProcessingResult> CompressAsync(
            string inputPath,
            string outputPath,
            int compressionPercentage,
            string videoCodec,
            string audioCodec,
            IProgress<ProgressInfo> progress,
            CancellationToken ct = default,
            PauseToken pauseToken = default);

        Task<ProcessingResult> ConvertAsync(
            string inputPath,
            string outputPath,
            string? videoCodec,
            string? audioCodec,
            string selectedFormat,
            IProgress<ProgressInfo> progress,
            CancellationToken ct = default,
            PauseToken pauseToken = default);

        Task<ProcessingResult> AudiofyAsync(
            string inputPath,
            string outputPath,
            string? videoCodec,
            string? audioCodec,
            string selectedAudioFormat,
            IProgress<ProgressInfo> progress, 
            CancellationToken ct = default,
            PauseToken pauseToken = default);
    }
}