using System;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Models;

namespace VManager.Services.Core.Execution
{
    internal interface IResumableFFmpegExecutor
    {
        Task<ProcessingResult> ExecuteResumableAsync(
            string inputPath,
            string outputPath,
            Func<FFMpegArgumentOptions, FFMpegArgumentOptions> args,
            double duration,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken,
            PauseToken pauseToken = default,
            string? operationName = null);
    }
}