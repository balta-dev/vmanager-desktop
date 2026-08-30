// Services/Operations/VMotionOperation.cs
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
    internal class MotionOperation
    {
        private readonly IFFmpegExecutor _executor;
        private readonly IMediaAnalyzer _analyzer;

        public MotionOperation(string ffmpegPath)
        {
            _executor = new FFmpegExecutor(ffmpegPath);
            _analyzer = new MediaAnalyzer();
        }

        // Para tests
        public MotionOperation(IFFmpegExecutor executor, IMediaAnalyzer analyzer)
        {
            _executor = executor;
            _analyzer = analyzer;
        }

        /// <summary>
        /// Cambia FPS y/o velocidad de reproducción de un archivo de video o audio.
        /// </summary>
        /// <param name="inputPath">Ruta del archivo de entrada.</param>
        /// <param name="outputPath">Ruta del archivo de salida.</param>
        /// <param name="targetFps">FPS deseados, null = no modificar (o si es audio).</param>
        /// <param name="speed">Factor de velocidad (1.0 = sin cambios, 2.0 = doble velocidad, 0.5 = mitad).</param>
        /// <param name="isAudio">true si el archivo es solo audio (omite cambio de FPS).</param>
        /// <param name="progress">Reporte de progreso.</param>
        /// <param name="cancellationToken">Token de cancelación.</param>
        /// <param name="pauseToken">Token de pausa.</param>
        public async Task<ProcessingResult> ExecuteAsync(
            string inputPath,
            string outputPath,
            double? targetFps,
            double speed,
            bool isAudio,
            IProgress<IFFmpegProcessor.ProgressInfo> progress,
            CancellationToken cancellationToken = default,
            PauseToken pauseToken = default)
        {
            inputPath  = OutputPathBuilder.SanitizeFilename(inputPath);
            outputPath = OutputPathBuilder.SanitizeFilename(outputPath);

            // Analizar duración para el progreso
            var analysisResult = await _analyzer.AnalyzeAsync(inputPath);
            if (!analysisResult.Success)
                return new ProcessingResult(false, analysisResult.Message);

            var mediaInfo = analysisResult.Result!;
            double duration = mediaInfo.Duration.TotalSeconds;

            // Construir filtros
            // setpts cambia la velocidad del video: PTS = PTS * (1/speed)
            // atempo cambia la velocidad del audio (soporta 0.5 a 2.0; para valores fuera de rango se encadena)
            var videoFilters = BuildVideoFilters(targetFps, speed, isAudio);
            var audioFilters = BuildAudioFilters(speed);

            Console.WriteLine($"[VMotion] fps={targetFps?.ToString() ?? "no change"}, speed={speed}, isAudio={isAudio}");
            Console.WriteLine($"[VMotion] vf={videoFilters}, af={audioFilters}");

            var args = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, options =>
                {
                    options.WithCustomArgument("-map 0:v?");
                    options.WithCustomArgument("-map 0:a");
                    
                    if (!isAudio && !string.IsNullOrEmpty(videoFilters))
                        options.WithCustomArgument($"-vf \"{videoFilters}\"");

                    if (!string.IsNullOrEmpty(audioFilters))
                        options.WithCustomArgument($"-af \"{audioFilters}\"");

                    // Si no hay ningún filtro de video (solo audio), copiar stream de video si existe
                    if (!isAudio && string.IsNullOrEmpty(videoFilters))
                        options.WithCustomArgument("-c:v copy");
                });

            return await _executor.ExecuteAsync(
                inputPath,
                outputPath,
                args,
                duration,
                progress,
                cancellationToken,
                pauseToken);
        }

        // ── Helpers de filtros ────────────────────────────────────────────────

        private static string BuildVideoFilters(double? targetFps, double speed, bool isAudio)
        {
            if (isAudio) return "";

            var filters = new System.Collections.Generic.List<string>();

            // Cambio de FPS (solo interpolación/drop de frames, sin cambiar velocidad)
            // TODO: cuando se agregue interpolador (minterpolate), reemplazar fps= por el filtro correspondiente
            if (targetFps.HasValue)
                filters.Add($"fps={targetFps.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");

            // Velocidad de video: setpts
            if (Math.Abs(speed - 1.0) > 0.001)
            {
                double pts = 1.0 / speed;
                filters.Add($"setpts={pts.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}*PTS");
            }

            return string.Join(",", filters);
        }

        private static string BuildAudioFilters(double speed)
        {
            if (Math.Abs(speed - 1.0) <= 0.001) return "";

            // atempo solo acepta valores entre 0.5 y 2.0
            // Para valores fuera del rango se encadena: ej. 4x = atempo=2.0,atempo=2.0
            var filters = new System.Collections.Generic.List<string>();
            double remaining = speed;

            while (remaining > 2.0)
            {
                filters.Add("atempo=2.0");
                remaining /= 2.0;
            }
            while (remaining < 0.5)
            {
                filters.Add("atempo=0.5");
                remaining /= 0.5;
            }

            filters.Add($"atempo={remaining.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");

            return string.Join(",", filters);
        }
    }
}
