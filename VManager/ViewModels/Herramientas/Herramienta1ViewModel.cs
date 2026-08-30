using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using ReactiveUI;
using VManager.Services;
using VManager.Services.Core;
using VManager.Services.Core.Media;

namespace VManager.ViewModels.Herramientas
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public class Herramienta1ViewModel : ViewModelBase
    {
        
        private string _tiempoDesde = ""; 
        private string _tiempoHasta = ""; 
        public string TiempoDesde
        {
            get => _tiempoDesde;
            set
            {
                this.RaiseAndSetIfChanged(ref _tiempoDesde, value);
        
                if (TryParseTime(value, out TimeSpan result))
                {
                    double segundos = result.TotalSeconds;
                    // Solo movemos el slider si el cambio es real y no excede la duración
                    if (segundos <= VideoDuration && Math.Abs(_sliderDesde - segundos) > 0.1)
                    {
                        _sliderDesde = segundos; // Seteo directo al field para evitar bucles
                        this.RaisePropertyChanged(nameof(SliderDesde));
                
                        _lastThumbValue = segundos;
                        UpdatePopupOffset();
                    }
                }
            }
        }

        public string TiempoHasta
        {
            get => _tiempoHasta;
            set
            {
                this.RaiseAndSetIfChanged(ref _tiempoHasta, value);
        
                if (TryParseTime(value, out TimeSpan result))
                {
                    double segundos = result.TotalSeconds;
                    if (segundos <= VideoDuration && Math.Abs(_sliderHasta - segundos) > 0.1)
                    {
                        _sliderHasta = segundos;
                        this.RaisePropertyChanged(nameof(SliderHasta));
                
                        _lastThumbValue = segundos;
                        UpdatePopupOffset();
                    }
                }
            }
        }
        
        private bool _isConverting;
        public bool IsConverting
        {
            get => _isConverting;
            set => this.RaiseAndSetIfChanged(ref _isConverting, value);
        }
        
        protected override bool AllowAudioFiles => true;
        public ReactiveCommand<Unit, Unit> CutCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseSingleVideoCommand { get; }

        // --- Preview de frame ---
        private double _videoDuration = 1;
        public double VideoDuration
        {
            get => _videoDuration;
            set => this.RaiseAndSetIfChanged(ref _videoDuration, value);
        }

        private string _videoDurationFormatted = "0:00:00";
        public string VideoDurationFormatted
        {
            get => _videoDurationFormatted;
            set => this.RaiseAndSetIfChanged(ref _videoDurationFormatted, value);
        }

        private Bitmap? _previewFrame;
        public Bitmap? PreviewFrame
        {
            get => _previewFrame;
            set => this.RaiseAndSetIfChanged(ref _previewFrame, value);
        }

        private string _previewTimestamp = "";
        public string PreviewTimestamp
        {
            get => _previewTimestamp;
            set => this.RaiseAndSetIfChanged(ref _previewTimestamp, value);
        }

        // --- RangeSlider: thumb izquierdo (Desde) ---
        private double _sliderDesde;
        public double SliderDesde
        {
            get => _sliderDesde;
            set
            {
                this.RaiseAndSetIfChanged(ref _sliderDesde, value);
                TiempoDesde = ToTimestamp(value);
                _ = OnSliderChangedAsync(value);
            }
        }

        // --- RangeSlider: thumb derecho (Hasta) ---
        private double _sliderHasta = 1;
        public double SliderHasta
        {
            get => _sliderHasta;
            set
            {
                this.RaiseAndSetIfChanged(ref _sliderHasta, value);
                TiempoHasta = ToTimestamp(value);
                _ = OnSliderChangedAsync(value);
            }
        }
        
        private double _sliderTrackWidth = 400;
        public double SliderTrackWidth
        {
            get => _sliderTrackWidth;
            set
            {
                if (double.IsNaN(value) || value <= 0) return; // evita el crash de Arrange
                _sliderTrackWidth = value;
                UpdatePopupOffset();
            }
        }

        // Offset horizontal del Popup — se calcula en base al thumb activo
        private double _previewPopupHorizontalOffset;
        public double PreviewPopupHorizontalOffset
        {
            get => _previewPopupHorizontalOffset;
            set => this.RaiseAndSetIfChanged(ref _previewPopupHorizontalOffset, value);
        }

        private const double PopupWidth = 200;
        
        public double LastThumbValue
        {
            get => _lastThumbValue;
            set
            {
                this.RaiseAndSetIfChanged(ref _lastThumbValue, value);
                UpdatePopupOffset();
            }
        }

        private const double ThumbRadius = 18; // ajustá según el tamaño real del thumb del RangeSlider

        private void UpdatePopupOffset()
        {
            if (VideoDuration <= 0 || _sliderTrackWidth <= 0 || double.IsNaN(_sliderTrackWidth)) return;

            double ratio = Math.Clamp(_lastThumbValue / VideoDuration, 0.0, 1.0);
    
            // El thumb se mueve solo por el área efectiva, no por el ancho total
            double effectiveWidth = _sliderTrackWidth - (ThumbRadius * 2);
            double thumbX = ThumbRadius + (ratio * effectiveWidth);

            PreviewPopupHorizontalOffset = thumbX - (PopupWidth / 2.0);
        }

        // Último valor movido (Desde o Hasta)
        private double _lastThumbValue;

        private static string ToTimestamp(double seconds)
            => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");

        // --- Drag loop ---
        private bool _isDragging;
        private CancellationTokenSource? _dragLoopCts;

        public void StartDragging()
        {
            if (_isDragging) return;
            _isDragging = true;
            _dragLoopCts?.Cancel();
            _dragLoopCts = new CancellationTokenSource();
            _ = DragLoopAsync(_dragLoopCts.Token);
        }

        public void StopDragging()
        {
            _isDragging = false;
            _dragLoopCts?.Cancel();
            _ = ClearPreviewAfterDelayAsync();
        }

        private async Task DragLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var seconds = _lastThumbValue;
                    PreviewTimestamp = ToTimestamp(seconds);
                    var frame = await ExtractFrameAsync(VideoPath, seconds, token);
                    if (frame != null)
                        PreviewFrame = frame;
                    await Task.Delay(150, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ClearPreviewAfterDelayAsync()
        {
            await Task.Delay(600);
            if (!_isDragging)
            {
                PreviewFrame = null;
                PreviewTimestamp = "";
            }
        }

        private async Task OnSliderChangedAsync(double seconds)
        {
            _lastThumbValue = seconds;
            UpdatePopupOffset();
            // El drag loop se encarga del render mientras se arrastra
            await Task.CompletedTask;
        }


        private static async Task<Bitmap?> ExtractFrameAsync(string videoPath, double seconds, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(videoPath)) return null;

            var tempFile = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vmanager_preview_{Guid.NewGuid():N}.jpg");

            string ffmpeg = FFmpegManager.FfmpegPath;
            System.Diagnostics.Process? proc = null;

            try
            {
                var args = $"-ss {seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} " +
                           $"-i \"{videoPath}\" -frames:v 1 -q:v 3 \"{tempFile}\" -y";
                
                var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
                {
                    RedirectStandardError = false,
                    RedirectStandardOutput = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;

                // Registramos un callback en el token: si se cancela, matamos a FFmpeg inmediatamente.
                using var cancellationRegistration = token.Register(() => 
                {
                    try
                    {
                        if (!proc.HasExited) proc.Kill();
                    }
                    catch { /* Ignorar errores al intentar matar un proceso que ya murió */ }
                });

                await proc.WaitForExitAsync(token);

                // Verificamos de nuevo si nos cancelaron antes de empezar a leer el disco
                token.ThrowIfCancellationRequested();

                if (System.IO.File.Exists(tempFile))
                {
                    var info = new System.IO.FileInfo(tempFile);
                    if (info.Length == 0) return null; 

                    // Lectura rápida de I/O
                    var bytes = await System.IO.File.ReadAllBytesAsync(tempFile, token);
                    
                    // Volvemos a chequear antes de hacer el trabajo pesado de CPU
                    token.ThrowIfCancellationRequested();

                    // Mover la creación del Bitmap (que es puro CPU) a un hilo de fondo
                    return await Task.Run(() => 
                    {
                        using var ms = new System.IO.MemoryStream(bytes);
                        return new Bitmap(ms); // Se crea en el background, Avalonia lo acepta
                    }, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Se canceló porque el usuario movió/soltó el slider. Propagamos la excepción
                // para que DragLoopAsync la ignore limpiamente.
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                // Asegurar que el proceso se libere de la memoria gestionada
                proc?.Dispose();

                if (System.IO.File.Exists(tempFile))
                {
                    try { System.IO.File.Delete(tempFile); } 
                    catch { /* Ignorar fallos de borrado si el SO lo bloqueó brevemente */ }
                }
            }
            return null;
        }
        
        public Herramienta1ViewModel()
        {
            CutCommand = ReactiveCommand.CreateFromTask(CutVideo, outputScheduler: AvaloniaScheduler.Instance);
            BrowseSingleVideoCommand = ReactiveCommand.CreateFromTask(BrowseSingleVideo, outputScheduler: AvaloniaScheduler.Instance);
        }
        
        private async Task BrowseSingleVideo()
        {
            TopLevel? topLevel = null;

            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            }

            if (topLevel == null)
            {
                Status = L["VCut.Fields.NoMainWindow"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            var videoPatterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.wmv", "*.flv", "*.3gp" };
            var audioPatterns = new[] { "*.mp3", "*.ogg", "*.flac", "*.aac", ".wav", ".wma" };

            var filters = new List<FilePickerFileType>
            {
                new FilePickerFileType("Videos") { Patterns = videoPatterns }
            };
            
            if (AllowAudioFiles)
            {
                filters.Add(new FilePickerFileType("Audios") { Patterns = audioPatterns });
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L["VCut.Fields.BrowseTitle"],
                FileTypeFilter = filters,
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                VideoPath = files[0].Path.LocalPath;
                this.RaisePropertyChanged(nameof(VideoPath));
                IsVideoPathSet = true;
                this.RaisePropertyChanged(nameof(IsVideoPathSet));
                await LoadVideoDurationAsync(VideoPath);
            }
        }

        public async Task LoadVideoDurationAsync(string path)
        {
            string ffprobe = FFmpegManager.FfprobePath;
            try
            {
                // Usar ffprobe para obtener la duración en segundos
                var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"";
                var psi = new System.Diagnostics.ProcessStartInfo(ffprobe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return;

                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (double.TryParse(output.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double duration) && duration > 0)
                {
                    VideoDuration = duration;
                    VideoDurationFormatted = ToTimestamp(duration);
                    // Inicializar Hasta al final del video
                    _sliderHasta = duration;
                    this.RaisePropertyChanged(nameof(SliderHasta));
                    TiempoHasta = ToTimestamp(duration);
                    // Resetear Desde a 0
                    _sliderDesde = 0;
                    this.RaisePropertyChanged(nameof(SliderDesde));
                    TiempoDesde = ToTimestamp(0);
                    PreviewFrame = null;
                    PreviewTimestamp = "";
                }
            }
            catch { Console.WriteLine("ERROR - NO SE PUDO LEER INFORMACIÓN DEL VIDEO. Revisa que el video no esté corrupto y que ffprobe esté funcionando correctamente."); /* ffprobe no disponible */ }
        }

        private bool TryParseTime(string input, out TimeSpan result)
        {
            
            result = TimeSpan.Zero;
            // Si el usuario no escribió nada, lo tomamos como 0 y es un éxito
            if (string.IsNullOrWhiteSpace(input))
                return true;

            // Intentar parsear formatos: "10" → segundos, "3:45" → minutos:segundos
            string[] parts = input.Split(':');

            try
            {
                if (parts.Length == 1)
                {
                    // Solo segundos
                    if (double.TryParse(parts[0], out double seconds))
                    {
                        result = TimeSpan.FromSeconds(seconds);
                        return true;
                    }
                }
                else if (parts.Length == 2)
                {
                    // Minutos:Segundos
                    if (int.TryParse(parts[0], out int minutes) &&
                        double.TryParse(parts[1], out double seconds))
                    {
                        result = new TimeSpan(0, 0, minutes, 0) + TimeSpan.FromSeconds(seconds);
                        return true;
                    }
                }
                else if (parts.Length == 3)
                {
                    // Horas:Minutos:Segundos
                    if (int.TryParse(parts[0], out int hours) &&
                        int.TryParse(parts[1], out int minutes) &&
                        double.TryParse(parts[2], out double seconds))
                    {
                        result = new TimeSpan(hours, minutes, (int)seconds);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
        
        public override void ClearInfo()
        {
            base.ClearInfo();
            
            TiempoDesde = "";
            TiempoHasta = "";
            IsConverting = false;
            PreviewFrame = null;
            PreviewTimestamp = "";

            this.RaisePropertyChanged(nameof(TiempoDesde));
            this.RaisePropertyChanged(nameof(TiempoHasta));
            this.RaisePropertyChanged(nameof(IsConverting));
            
            _sliderDesde = 0;
            _sliderHasta = 1;
            this.RaisePropertyChanged(nameof(SliderDesde));
            this.RaisePropertyChanged(nameof(SliderHasta));
            
            VideoDuration = 1;
            VideoDurationFormatted = "0:00:00";
        }
        
        private async Task CutVideo()
        {
            HideFileReadyButton();
            _cts = new CancellationTokenSource();

            if (!TryParseTime(TiempoDesde, out TimeSpan start))
            {
                _ = SoundManager.Play("fail.wav");
                Status = L["VCut.Fields.InvalidFrom"];
                TiempoDesde = "";
                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(TiempoDesde));
                return;
            }

            Status = L["VCut.Fields.ObtainingInfo"];
            this.RaisePropertyChanged(nameof(Status));

            IFFmpegProcessor processor = new FFmpegProcessor();
            var analysisResult = await processor.AnalyzeVideoAsync(VideoPath);

            if (!analysisResult.Success)
            {
                _ = SoundManager.Play("fail.wav");
                Status = string.Format(L["VCut.Fields.Error"], analysisResult.Message);
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            var mediaInfo = analysisResult.Result!;
            double totalDuration = mediaInfo.Duration.TotalSeconds;

            TimeSpan duration;

            if (string.IsNullOrWhiteSpace(TiempoHasta))
            {
                duration = TimeSpan.FromSeconds(totalDuration - start.TotalSeconds);
                duration += TimeSpan.FromSeconds(1);

                if (duration <= TimeSpan.Zero)
                {
                    _ = SoundManager.Play("fail.wav");
                    Status = L["VCut.Fields.ExceedsLength"];
                    this.RaisePropertyChanged(nameof(Status));
                    return;
                }
            }
            else
            {
                if (!TryParseTime(TiempoHasta, out TimeSpan end))
                {
                    _ = SoundManager.Play("fail.wav");
                    Status = L["VCut.Fields.InvalidTo"];
                    TiempoHasta = "";
                    this.RaisePropertyChanged(nameof(Status));
                    this.RaisePropertyChanged(nameof(TiempoHasta));
                    return;
                }

                if (end <= start)
                {
                    _ = SoundManager.Play("fail.wav");
                    Status = L["VCut.Fields.EndBeforeStart"];
                    this.RaisePropertyChanged(nameof(Status));
                    return;
                }

                duration = end - start;
            }

            using var effectivePause = CreateEffectivePauseToken();

            try
            {
                var progress = new Progress<IFFmpegProcessor.ProgressInfo>(info =>
                {
                    Progress = (int)(info.Progress * 100);
                    RemainingTime = info.Remaining.ToString(@"mm\:ss");
                    this.RaisePropertyChanged(nameof(Progress));
                    this.RaisePropertyChanged(nameof(RemainingTime));
                });

                Status = L["VCut.Fields.Cutting"];
                IsConverting = true;
                IsOperationRunning = true;

                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(IsConverting));
                this.RaisePropertyChanged(nameof(IsOperationRunning));
                
                string OutputPath = OutputPathBuilder.GetCutOutputPath(VideoPath, start, duration);

                var result = await processor.CutAsync(
                    VideoPath,
                    OutputPath,
                    start,
                    duration,
                    progress,
                    _cts.Token,
                    effectivePause.Token
                );

                if (result.Success)
                {
                    NotificationService notificationService = new NotificationService();
                    notificationService.ShowFileConvertedNotification(
                        string.Format(L["VCut.Fields.NotificationMessage"], result.Message),
                        result.OutputPath
                    );

                    _ = SoundManager.Play("sucess-final.wav");
                    SetLastCompressedFile(result.OutputPath);

                    Status = string.Format(L["VCut.Fields.Completed"], result.Message);
                    Warning = result.Warning;
                    Progress = 100;
                    OutputPath = string.Format(L["VCut.Fields.OutputLabel"], result.OutputPath);

                    this.RaisePropertyChanged(nameof(Status));
                    this.RaisePropertyChanged(nameof(Progress));
                    this.RaisePropertyChanged(nameof(OutputPath));
                    this.RaisePropertyChanged(nameof(Warning));
                }
                else
                {
                    _ = SoundManager.Play("fail.wav");
                    Status = string.Format(L["VCut.Fields.Error"], result.Message);
                    Progress = 0;

                    this.RaisePropertyChanged(nameof(Status));
                    this.RaisePropertyChanged(nameof(Progress));
                }
            }
            catch (OperationCanceledException)
            {
                await SoundManager.Play("fail.wav");
                Status = L["VCut.Fields.CutCanceled"];
                Progress = 0;

                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(Progress));
            }
            finally
            {
                IsConverting = false;
                IsOperationRunning = false;
                this.RaisePropertyChanged(nameof(IsConverting));
                this.RaisePropertyChanged(nameof(IsOperationRunning));

                _cts?.Dispose();
                _cts = null;
            }
        }
        
    }
}