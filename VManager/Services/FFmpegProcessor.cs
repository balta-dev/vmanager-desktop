// Services/FFmpegProcessor.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Models;
using VManager.Services.Operations;
using VManager.Services.Core.Media;
using VManager.Services.Core.Execution;

namespace VManager.Services
{
    public class FFmpegProcessor : IFFmpegProcessor
    {
        private readonly string _ffmpegPath = FFmpegManager.FfmpegPath;

        private readonly CutOperation _cut;
        private readonly CompressOperation _compress;
        private readonly ConvertOperation _convert;
        private readonly AudioExtractOperation _audioExtract;

        public FFmpegProcessor()
        {
            _cut = new CutOperation(_ffmpegPath);
            _compress = new CompressOperation(_ffmpegPath);
            _convert = new ConvertOperation(_ffmpegPath);
            _audioExtract = new AudioExtractOperation(_ffmpegPath);
        }

        public async Task<AnalysisResult<IMediaAnalysis>> AnalyzeVideoAsync(string inputPath)
            => await new MediaAnalyzer().AnalyzeAsync(inputPath);

        public async Task<ProcessingResult> CutAsync(string inputPath, string outputPath, TimeSpan start, TimeSpan duration, IProgress<IFFmpegProcessor.ProgressInfo> progress, CancellationToken ct = default, PauseToken pauseToken = default)
            => await _cut.ExecuteAsync(inputPath, outputPath, start, duration, progress, ct, pauseToken);

        public async Task<ProcessingResult> CompressAsync(string inputPath, string outputPath, int compressionPercentage, string? videoCodec, string? audioCodec, IProgress<IFFmpegProcessor.ProgressInfo> progress, CancellationToken ct = default, PauseToken pauseToken = default)
            => await _compress.ExecuteAsync(inputPath, outputPath, compressionPercentage, videoCodec, audioCodec, progress, ct, pauseToken);

        public async Task<ProcessingResult> ConvertAsync(string inputPath, string outputPath, string? videoCodec, string? audioCodec, string selectedFormat, IProgress<IFFmpegProcessor.ProgressInfo> progress, CancellationToken ct = default, PauseToken pauseToken = default)
            => await _convert.ExecuteAsync(inputPath, outputPath, videoCodec, audioCodec, selectedFormat, progress, ct, pauseToken);

        public async Task<ProcessingResult> AudiofyAsync(string inputPath, string outputPath, string? videoCodec, string? audioCodec, string selectedAudioFormat, IProgress<IFFmpegProcessor.ProgressInfo> progress, CancellationToken ct = default, PauseToken pauseToken = default)
            => await _audioExtract.ExecuteAsync(inputPath, outputPath, videoCodec, audioCodec, selectedAudioFormat, progress, ct, pauseToken);
    }
}