using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using VManager.Services.Models;

namespace VManager.Services.Core.Execution;

internal class ResumableFFmpegExecutor : IResumableFFmpegExecutor
{
    private readonly string _ffmpegPath;
    private const int ChunkDurationSeconds = 60;
    private const int MaxParallelProcessing = 2; // Procesar máximo 2 chunks a la vez

    public ResumableFFmpegExecutor(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    public virtual async Task<ProcessingResult> ExecuteResumableAsync(
        string inputPath,
        string outputPath,
        Func<FFMpegArgumentOptions, FFMpegArgumentOptions> configureOptions,
        double totalDuration,
        IProgress<IFFmpegProcessor.ProgressInfo> progress,
        CancellationToken ct,
        PauseToken pt = default,
        string? operationName = null)
    {
        string tempFolder = ResumeManager.GetTempFolder(inputPath);
        string chunkFolder = Path.Combine(tempFolder, "chunks");
        string processedFolder = Path.Combine(tempFolder, "processed");
        string originalExtension = Path.GetExtension(inputPath);
        string outputExtension = Path.GetExtension(outputPath);

        try
        {
            Directory.CreateDirectory(chunkFolder);
            Directory.CreateDirectory(processedFolder);

            var progressLog = ResumeManager.LoadProgress(inputPath);
            var completedChunks = progressLog?.CompletedChunks ?? new List<int>();
            int estimatedTotalChunks = (int)Math.Ceiling(totalDuration / ChunkDurationSeconds);
            
            // Variable compartida para saber si el split terminó
            bool splitCompleted = false;
            int actualTotalChunks = 0;
            var splitLock = new object();

            // TAREA 1: Split en background (si no está hecho)
            var splitTask = Task.Run(async () =>
            {
                if (!Directory.EnumerateFiles(chunkFolder, $"vmanager_chunk*{originalExtension}").Any())
                {
                    Console.WriteLine("[DEBUG] Iniciando split de video...");
                    var splitResult = await SplitIntoChunks(inputPath, chunkFolder, originalExtension, ct, pt);
                    
                    if (!splitResult.Success)
                    {
                        CleanupTempFolder(tempFolder);
                        return splitResult;
                    }
                }
                
                lock (splitLock)
                {
                    actualTotalChunks = Directory.GetFiles(chunkFolder, $"vmanager_chunk*{originalExtension}").Length;
                    splitCompleted = true;
                }
                
                Console.WriteLine($"[DEBUG] Split completado. Total chunks: {actualTotalChunks}");
                return new ProcessingResult(true, "Split OK");
            }, ct);

            // TAREA 2: Procesamiento de chunks (comienza apenas hay chunks disponibles)
            var semaphore = new SemaphoreSlim(MaxParallelProcessing);
            var processingTasks = new List<Task<ProcessingResult>>();
            int currentChunk = 1;

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    CleanupTempFolder(tempFolder);
                    return new ProcessingResult(false, "Operación cancelada por el usuario.");
                }

                string chunkInput = Path.Combine(chunkFolder, $"vmanager_chunk{currentChunk:D03}{originalExtension}");
                string chunkOutput = Path.Combine(processedFolder, $"processed_chunk{currentChunk:D03}{outputExtension}");

                // Esperar a que el chunk exista (lo está creando el split)
                bool chunkExists = File.Exists(chunkInput);
                
                if (!chunkExists)
                {
                    // Si el split ya terminó y el chunk no existe, salimos del loop
                    bool splitDone;
                    lock (splitLock) { splitDone = splitCompleted; }
                    
                    if (splitDone) break;
                    
                    // Esperar un poco antes de volver a revisar
                    await Task.Delay(500, ct);
                    continue;
                }

                // El chunk existe, verificar si ya está procesado
                if (completedChunks.Contains(currentChunk) && ResumeManager.IsChunkValid(chunkOutput))
                {
                    currentChunk++;
                    continue;
                }

                // Chunk pendiente, procesarlo
                completedChunks.Remove(currentChunk);
                if (File.Exists(chunkOutput)) File.Delete(chunkOutput);

                int chunkNumber = currentChunk;
                
                // Procesar chunk en paralelo (máximo MaxParallelProcessing a la vez)
                await semaphore.WaitAsync(ct);
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        Console.WriteLine($"[DEBUG] Procesando chunk {chunkNumber}...");
                        
                        var args = FFMpegArguments.FromFileInput(chunkInput)
                            .OutputToFile(chunkOutput, overwrite: true, options => configureOptions(options));

                        int totalChunks;
                        lock (splitLock) { totalChunks = splitCompleted ? actualTotalChunks : estimatedTotalChunks; }

                        var adjustedProgress = new Progress<IFFmpegProcessor.ProgressInfo>(p =>
                        {
                            int completedCount = completedChunks.Count;
                            double baseProgress = (double)completedCount / totalChunks;
                            double chunkProgress = p.Progress;
                            double chunkWeight = 1.0 / totalChunks;
                            double totalProgress = baseProgress + (chunkProgress * chunkWeight);
                            
                            TimeSpan remainingTime = p.Remaining;
                            if (completedCount < totalChunks - 1)
                            {
                                int chunksLeft = totalChunks - completedCount - 1;
                                remainingTime += TimeSpan.FromSeconds(ChunkDurationSeconds * chunksLeft);
                            }

                            progress?.Report(new IFFmpegProcessor.ProgressInfo(totalProgress, remainingTime));
                        });

                        var result = await new FFmpegExecutor(_ffmpegPath).ExecuteAsync(
                            chunkInput, chunkOutput, args, ChunkDurationSeconds, adjustedProgress, ct, pt);

                        if (result.Success)
                        {
                            lock (completedChunks)
                            {
                                completedChunks.Add(chunkNumber);
                                ResumeManager.SaveProgress(inputPath, new ResumeProgress 
                                { 
                                    CompletedChunks = completedChunks.OrderBy(x => x).ToList() 
                                });
                            }
                            Console.WriteLine($"[DEBUG] Chunk {chunkNumber} completado.");
                        }

                        return result;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);

                processingTasks.Add(task);
                currentChunk++;
            }

            // Esperar a que terminen todas las tareas
            await Task.WhenAll(processingTasks);
            
            // Verificar si algún chunk falló
            foreach (var task in processingTasks)
            {
                var result = await task;
                if (!result.Success)
                {
                    CleanupTempFolder(tempFolder);
                    return result;
                }
            }

            // Verificar que el split también terminó bien
            var splitResult = await splitTask;
            if (!splitResult.Success)
            {
                CleanupTempFolder(tempFolder);
                return splitResult;
            }

            if (ct.IsCancellationRequested)
            {
                CleanupTempFolder(tempFolder);
                return new ProcessingResult(false, "Operación cancelada por el usuario.");
            }

            // CONCATENACIÓN FINAL
            var concatResult = await ConcatenateChunks(processedFolder, actualTotalChunks, outputPath, outputExtension, ct, pt);
            
            if (concatResult.Success)
            {
                ResumeManager.ClearProgress(inputPath);
                CleanupTempFolder(tempFolder);
            }
            else
            {
                CleanupTempFolder(tempFolder);
            }

            return concatResult;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFolder(tempFolder);
            return new ProcessingResult(false, "Operación cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            CleanupTempFolder(tempFolder);
            throw;
        }
    }
    
    private void CleanupTempFolder(string tempFolder)
    {
        try
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
                Console.WriteLine($"[DEBUG] Carpeta temporal eliminada: {tempFolder}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] No se pudo eliminar carpeta temporal: {ex.Message}");
            ErrorService.Show(ex);
        }
    }

    private async Task<ProcessingResult> SplitIntoChunks(string inputPath, string chunkFolder, string extension, CancellationToken ct, PauseToken pt)
    {
        string pattern = Path.Combine(chunkFolder, $"vmanager_chunk%03d{extension}");
        
        var args = FFMpegArguments.FromFileInput(inputPath)
            .OutputToFile(pattern, true, opt => opt
                .WithCustomArgument("-f segment")
                .WithCustomArgument($"-segment_time {ChunkDurationSeconds}")
                .WithCustomArgument("-segment_start_number 1")
                .WithCustomArgument("-map 0")
                .WithCustomArgument("-c copy")
                .WithCustomArgument("-avoid_negative_ts make_zero")
                .WithCustomArgument("-reset_timestamps 1"));

        var result = await new FFmpegExecutor(_ffmpegPath).ExecuteAsync(inputPath, pattern, args, 0, null!, ct, pt);

        if (!result.Success && Directory.EnumerateFiles(chunkFolder).Any())
            return new ProcessingResult(true, "Split OK", pattern);

        return result;
    }

    private async Task<ProcessingResult> ConcatenateChunks(string processedFolder, int totalChunks, string finalOutput, string extension, CancellationToken ct, PauseToken pt)
    {
        string tempFolder = Path.GetDirectoryName(processedFolder)!;
        string listPath = Path.Combine(tempFolder, "concat_list.txt");

        var validLines = new List<string>();
        for (int i = 1; i <= totalChunks; i++)
        {
            string chunkPath = Path.Combine(processedFolder, $"processed_chunk{i:D03}{extension}");
            if (ResumeManager.IsChunkValid(chunkPath))
                validLines.Add($"file '{Path.GetFullPath(chunkPath).Replace("\\", "/")}'");
        }

        if (validLines.Count < totalChunks)
            return new ProcessingResult(false, $"Integridad fallida: faltan {totalChunks - validLines.Count} fragmentos.");

        await File.WriteAllLinesAsync(listPath, validLines, ct);

        var args = FFMpegArguments.FromFileInput(listPath, true, opt => opt.WithCustomArgument("-f concat").WithCustomArgument("-safe 0"))
            .OutputToFile(finalOutput, true, opt => opt.WithCustomArgument("-c copy"));

        return await new FFmpegExecutor(_ffmpegPath).ExecuteAsync(listPath, finalOutput, args, 0, null!, ct, pt);
    }
}