using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Models;

namespace VManager.Services.Core.Execution;

internal class FFmpegExecutor : IFFmpegExecutor
{
    private readonly string _ffmpegPath;

    public FFmpegExecutor(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    public virtual async Task<ProcessingResult> ExecuteAsync(
        string inputPath,
        string outputPath,
        FFMpegArgumentProcessor args,
        double duration,
        IProgress<IFFmpegProcessor.ProgressInfo> progress,
        CancellationToken cancellationToken,
        PauseToken pauseToken = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args.Arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        void OnPauseChanged(bool paused)
        {
            if (process.HasExited) return;
            if (paused) ProcessSuspender.Suspend(process.Id);
            else ProcessSuspender.Resume(process.Id);
        }

        try
        {
            pauseToken.PauseChanged += OnPauseChanged;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.Token.Register(() =>
                {
                    if (!process.HasExited)
                    {
                        if (pauseToken.IsPaused) ProcessSuspender.Resume(process.Id); // por si estaba suspendido
                        process.Kill();
                        Console.WriteLine("[DEBUG]: Proceso FFmpeg terminado por cancelación.");
                        if (File.Exists(outputPath))
                        {
                            try
                            {
                                File.Delete(outputPath);
                                Console.WriteLine("[DEBUG]: Archivo de salida eliminado tras cancelación.");
                            }
                            catch (IOException ex)
                            {
                                Console.WriteLine($"[DEBUG]: No se pudo eliminar el archivo: {ex.Message}");
                            }
                        }
                    }
                });

                process.Start();
                
                // si ya arrancó pausado (edge case: overlay ya estaba activo)
                if (pauseToken.IsPaused)
                    OnPauseChanged(true);

                // ✅ Capturar TODA la salida de error mientras leemos línea por línea
                var errorOutputBuilder = new StringBuilder();

                using (var reader = process.StandardError)
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        // Guardar todas las líneas para diagnóstico
                        errorOutputBuilder.AppendLine(line);

                        if (line.Contains("time="))
                        {
                            var timeMatch = Regex.Match(line, @"time=(\d{2}:\d{2}:\d{2}\.\d{2})");
                            var speedMatch = Regex.Match(line, @"speed=(\d+(\.\d+)?)x");

                            if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups[1].Value, out var time))
                            {
                                double processed = time.TotalSeconds;
                                double progressValue = Math.Min(processed / duration, 1.0);

                                // tiempo restante del video
                                double remainingVideo = duration - processed;

                                // velocidad de procesamiento (1.0x = tiempo real)
                                double speed = speedMatch.Success
                                    ? double.Parse(speedMatch.Groups[1].Value,
                                        System.Globalization.CultureInfo.InvariantCulture)
                                    : 1.0;

                                // tiempo real estimado restante (ajustado por velocidad)
                                TimeSpan remainingReal = TimeSpan.FromSeconds(remainingVideo / speed);

                                // debug opcional
                                Console.WriteLine(
                                    $"[DEBUG] Progreso: {progressValue:P2}, Restante real: {remainingReal}");

                                // reportamos progreso usando tu ProgressInfo
                                progress?.Report(new IFFmpegProcessor.ProgressInfo(progressValue, remainingReal));
                            }
                        }
                    }
                }

                await process.WaitForExitAsync(cts.Token);

                if (cts.Token.IsCancellationRequested)
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    return new ProcessingResult(false, "Operación cancelada por el usuario.");
                }

                // ✅ Ahora usar el string capturado, no leer del stream cerrado
                string errorOutput = errorOutputBuilder.ToString();

                if (process.ExitCode != 0)
                {
                    if (errorOutput.Contains("No such file or directory") ||
                        errorOutput.Contains("Permission denied") ||
                        errorOutput.Contains("Could not create") ||
                        errorOutput.Contains("Invalid argument"))
                    {
                        throw new Exception($"FFmpeg error: {errorOutput} (ExitCode: {process.ExitCode})");
                    }

                    // Si hubo error pero no es crítico, también reportarlo
                    return new ProcessingResult(false, $"FFmpeg falló con código {process.ExitCode}: {errorOutput}");
                }

                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    return new ProcessingResult(false, "El archivo de salida no se creó correctamente.");
                }

                return new ProcessingResult(true, "¡Operación finalizada!", outputPath);
            }
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            return new ProcessingResult(false, "Operación cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG]: Error: {ex.Message}");
            ErrorService.Show(ex);
            return new ProcessingResult(false, $"Error: {ex.Message}");
        }
        finally
        {
            pauseToken.PauseChanged -= OnPauseChanged; // evitar leak del delegate
        }
    }
}