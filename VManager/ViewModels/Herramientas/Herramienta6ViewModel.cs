using Avalonia.ReactiveUI;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using VManager.Services;
using VManager.Services.Core;
using VManager.Services.Core.Media;
using VManager.Services.Operations;

namespace VManager.ViewModels.Herramientas
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public class Herramienta6ViewModel : CodecViewModelBase
    {
        protected override bool AllowAudioFiles => true;
        
        // ── Estado de operación ───────────────────────────────────────────────

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
        }

        // ── FPS ───────────────────────────────────────────────────────────────

        /// <summary>
        /// FPS detectados/ingresados. null = no aplicar cambio de FPS.
        /// Se bloquea si hay algún archivo de audio en la lista.
        /// </summary>
        private string _targetFps = "";
        public string TargetFps
        {
            get => _targetFps;
            set => this.RaiseAndSetIfChanged(ref _targetFps, value);
        }

        /// <summary>
        /// true cuando la lista contiene al menos un archivo de audio
        /// (o mezcla video+audio), lo que bloquea el campo de FPS.
        /// </summary>
        private bool _fpsLocked;
        public bool FpsLocked
        {
            get => _fpsLocked;
            set => this.RaiseAndSetIfChanged(ref _fpsLocked, value);
        }

        // ── Velocidad ─────────────────────────────────────────────────────────

        private string _speed = "1";
        public string Speed
        {
            get => _speed;
            set => this.RaiseAndSetIfChanged(ref _speed, value);
        }

        // ── Comandos ──────────────────────────────────────────────────────────

        public ReactiveCommand<Unit, Unit> ProcessCommand { get; }

        // ── Extensiones consideradas audio ────────────────────────────────────

        private static readonly string[] AudioExtensions =
            { ".mp3", ".aac", ".flac", ".ogg", ".opus", ".wav", ".m4a", ".wma", ".aiff", ".alac" };

        // ── Constructor ───────────────────────────────────────────────────────

        public Herramienta6ViewModel()
        {
            ProcessCommand = ReactiveCommand.CreateFromTask(
                ProcessFiles,
                outputScheduler: AvaloniaScheduler.Instance);

            // Cuando cambia la lista de archivos, recalcular FPS y FpsLocked
            this.WhenAnyValue(x => x.VideoPaths)
                .Subscribe(_ => RefreshFpsFromFiles());

            // También suscribirse a cambios en el contenido de la colección
            VideoPaths.CollectionChanged += (_, _) => RefreshFpsFromFiles();
        }
        
        public override void ClearInfo()
        {
            base.ClearInfo();
            FpsLocked = true;
            Speed = "1";
            TargetFps = "";
            this.RaisePropertyChanged(nameof(FpsLocked));
            this.RaisePropertyChanged(nameof(Speed));
            this.RaisePropertyChanged(nameof(TargetFps));
            
        }

        // ── Lógica FPS ────────────────────────────────────────────────────────

        private void RefreshFpsFromFiles()
        {
            if (VideoPaths.Count == 0)
            {
                FpsLocked = true;
                TargetFps = "";
                return;
            }

            // Si algún archivo es audio (o si hay mezcla), bloquear FPS
            bool hasAudio = VideoPaths.Any(p =>
                AudioExtensions.Contains(
                    Path.GetExtension(p).ToLowerInvariant()));

            FpsLocked = hasAudio;

            if (hasAudio)
            {
                TargetFps = "";
                return;
            }

            // Leer FPS de todos los videos y quedarse con el menor
            _ = Task.Run(async () =>
            {
                double minFps = double.MaxValue;
                var analyzer = new MediaAnalyzer();

                foreach (var path in VideoPaths.ToList())
                {
                    try
                    {
                        var result = await analyzer.AnalyzeAsync(path);
                        if (!result.Success) continue;

                        var fps = result.Result?.PrimaryVideoStream?.FrameRate ?? 0;
                        if (fps > 0 && fps < minFps)
                            minFps = fps;
                    }
                    catch
                    {
                        // Ignorar archivos que no se puedan analizar
                    }
                }

                var fpsStr = minFps == double.MaxValue
                    ? ""
                    : minFps.ToString("0.##");

                // Volver al hilo de UI
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TargetFps = fpsStr;
                });
            });
        }

        // ── Procesamiento ─────────────────────────────────────────────────────

        private async Task ProcessFiles()
        {
            HideFileReadyButton();
            _cts = new CancellationTokenSource();

            if (VideoPaths.Count == 0)
            {
                Status = L["VMotion.Fields.NoFiles"];
                return;
            }

            if (!double.TryParse(Speed.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double speedValue) || speedValue <= 0)
            {
                Status = L["VMotion.Fields.InvalidSpeed"];
                return;
            }

            double? fpsValue = null;
            if (!FpsLocked && !string.IsNullOrWhiteSpace(TargetFps))
            {
                if (!double.TryParse(TargetFps.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double parsedFps) || parsedFps <= 0)
                {
                    Status = L["VMotion.Fields.InvalidFps"];
                    return;
                }
                fpsValue = parsedFps;
            }

            using var effectivePause = CreateEffectivePauseToken();

            try
            {
                IsProcessing = true;
                IsOperationRunning = true;

                int totalFiles = VideoPaths.Count;
                int currentFileIndex = 0;
                int successCount = 0;

                var filesCopy = VideoPaths.ToList();

                foreach (var inputPath in filesCopy)
                {
                    currentFileIndex++;

                    Status = string.Format(
                        L["VMotion.Fields.Transforming"],
                        currentFileIndex,
                        totalFiles,
                        Path.GetFileName(inputPath));

                    var progress = new Progress<IFFmpegProcessor.ProgressInfo>(p =>
                    {
                        double globalProgress = ((currentFileIndex - 1) + p.Progress) / totalFiles;
                        Progress = (int)(globalProgress * 100);
                        RemainingTime = p.Remaining.ToString(@"mm\:ss");
                    });

                    bool isAudio = AudioExtensions.Contains(
                        Path.GetExtension(inputPath).ToLowerInvariant());

                    string outputPath = BuildOutputPath(inputPath, speedValue, fpsValue, isAudio);
                    var operation = new MotionOperation(FFmpegManager.FfmpegPath);
                    var result = await operation.ExecuteAsync(
                        inputPath,
                        outputPath,
                        fpsValue,
                        speedValue,
                        isAudio,
                        progress,
                        _cts.Token,
                        effectivePause.Token);

                    if (!result.Success)
                    {
                        _ = SoundManager.Play("fail.wav");
                        Status = string.Format(
                            L["VMotion.Fields.Error"],
                            Path.GetFileName(inputPath),
                            result.Message);
                        break;
                    }

                    successCount++;
                    bool isLast = currentFileIndex == totalFiles;
                    _ = SoundManager.Play(isLast ? "sucess-final.wav" : "sucess-partial.wav");
                    SetLastCompressedFile(result.OutputPath);

                    var notificationService = new NotificationService();
                    notificationService.ShowFileConvertedNotification(
                        string.Format(L["VMotion.Fields.NotificationMessage"], result.Message),
                        result.OutputPath);
                }

                Progress = 100;

                Status = successCount == totalFiles
                    ? string.Format(L["VMotion.Fields.CompletedAll"],
                        successCount,
                        successCount > 1 ? "s" : "",
                        successCount > 1 ? "s" : "")
                    : string.Format(L["VMotion.Fields.CompletedPartial"],
                        successCount, totalFiles);

                IsProcessing = false;
                IsOperationRunning = false;
                IsVideoPathSet = false;
            }
            catch (OperationCanceledException)
            {
                _ = SoundManager.Play("fail.wav");
                Status = L["VMotion.Fields.Canceled"];
                Progress = 0;
                IsProcessing = false;
                IsOperationRunning = false;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildOutputPath(
            string inputPath, double speed, double? fps, bool isAudio)
        {
            var dir = Path.GetDirectoryName(inputPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(inputPath);
            var ext = Path.GetExtension(inputPath);

            var suffix = "";
            if (fps.HasValue && !isAudio)
                suffix += $"_{fps.Value:0.##}fps";
            if (Math.Abs(speed - 1.0) > 0.001)
                suffix += $"_{speed:0.##}x";

            if (string.IsNullOrEmpty(suffix)) suffix = "_vmotion";

            return Path.Combine(dir, $"{name}{suffix}{ext}");
        }
    }
}
