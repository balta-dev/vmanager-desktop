using Avalonia.ReactiveUI;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using VManager.Services;
using VManager.Services.Core;
using VManager.Services.Core.Media;

namespace VManager.ViewModels.Herramientas
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public class Herramienta3ViewModel : CodecViewModelBase
    {
        private bool _isConverting;
        public bool IsConverting
        {
            get => _isConverting;
            set => this.RaiseAndSetIfChanged(ref _isConverting, value);
        }
        
        private VideoFormat _selectedFormat = new VideoFormat();
        public VideoFormat SelectedFormat
        {
            get => _selectedFormat;
            set => this.RaiseAndSetIfChanged(ref _selectedFormat, value);
        }
        
        public ReactiveCommand<Unit, Unit> ConvertCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCodecsCommand { get; } 
       
        private bool _isUpdatingCodecs = false;
        
        public Herramienta3ViewModel()
        {
            ConvertCommand = ReactiveCommand.CreateFromTask(ConvertVideo, outputScheduler: AvaloniaScheduler.Instance);
            SelectedFormat = SupportedVideoFormats[0]; //mp4
            _ = LoadCodecsAsync();
            
            // Suscripción única que maneja todos los cambios
            this.WhenAnyValue(
                x => x.SelectedVideoCodec, 
                x => x.SelectedAudioCodec,
                x => x.SelectedFormat)
                .Throttle(TimeSpan.FromMilliseconds(50)) // Evita múltiples updates rápidos
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(_ => UpdateCodecCompatibility());
        }
        
        /// <summary>
        /// Método principal que actualiza la compatibilidad de códecs basándose en el formato y códecs seleccionados
        /// </summary>
        private void UpdateCodecCompatibility()
        {
            if (_isUpdatingCodecs || _allVideoCodecs == null || _allAudioCodecs == null) return;
            
            _isUpdatingCodecs = true;
            
            try
            {
                // 1. Obtener códecs compatibles con el formato
                var (formatVideoCodecs, formatAudioCodecs) = GetCodecsForFormat(SelectedFormat?.Extension);
                
                // 2. Filtrar códecs de video compatibles con el audio seleccionado
                var compatibleVideoCodecs = formatVideoCodecs
                    .Where(v => IsVideoAudioCompatible(v, SelectedAudioCodec))
                    .ToList();
                
                // 3. Filtrar códecs de audio compatibles con el video seleccionado
                var compatibleAudioCodecs = formatAudioCodecs
                    .Where(a => IsVideoAudioCompatible(SelectedVideoCodec, a))
                    .ToList();
                
                // 4. Actualizar listas solo si hay cambios reales
                UpdateCollectionIfNeeded(AvailableVideoCodecs, compatibleVideoCodecs);
                UpdateCollectionIfNeeded(AvailableAudioCodecs, compatibleAudioCodecs);
                
                // 5. Validar y ajustar selecciones si es necesario
                EnsureValidSelection(
                    AvailableVideoCodecs, 
                    ref _selectedVideoCodec, 
                    nameof(SelectedVideoCodec)
                );
                
                EnsureValidSelection(
                    AvailableAudioCodecs, 
                    ref _selectedAudioCodec, 
                    nameof(SelectedAudioCodec)
                );
            }
            finally
            {
                _isUpdatingCodecs = false;
            }
        }
        
        /// <summary>
        /// Retorna los códecs compatibles con un formato específico
        /// </summary>
        private (List<string> video, List<string> audio) GetCodecsForFormat(string? format)
        {
            var videoCodecs = _allVideoCodecs.ToList();
            var audioCodecs = _allAudioCodecs.ToList();

            if (string.IsNullOrEmpty(format))
                return (videoCodecs, audioCodecs);
            
            var h264Encoders = new[] { "libx264", "h264", "h264_nvenc", "h264_vaapi", "h264_qsv", "h264_amf" };
            var h265Encoders = new[] { "libx265", "h265", "hevc", "hevc_nvenc", "hevc_vaapi", "hevc_qsv", "hevc_amf" };
            var vpEncoders   = new[] { "libvpx", "vp8", "vp9" };
            var av1Encoders  = new[] { "av1", "libsvtav1", "libaom-av1" }; // <--- nuevo
            var flvEncoders  = new[] { "flv" }; // fallback encoder si lo hay
            var mpeg4Enc     = new[] { "mpeg4" };
            var h263Enc      = new[] { "h263" };
            var dnxhrEnc     = new[] { "dnxhd", "dnxhr" };
            var aacEnc       = new[] { "aac" };
            var mp3Enc       = new[] { "libmp3lame" };
            var opusEnc      = new[] { "libopus" };
            var vorbisEnc    = new[] { "libvorbis" };
            var wmaEnc       = new[] { "wmav2" };
            var amrEnc       = new[] { "amr_nb", "amr_wb" };
            var pcmEnc       = new[] { "pcm_s16le", "pcm_s24le", "pcm_f32le" };

            // Helper para filtrar por grupo si existe en la lista real
            List<string> FilterEncoders(IEnumerable<string> source, IEnumerable<string> group) =>
                source.Where(v => group.Any(g => v.Contains(g, StringComparison.OrdinalIgnoreCase))).ToList();

            switch (format.ToLower())
            {
                case "mp4":
                    videoCodecs = FilterEncoders(videoCodecs, h264Encoders.Concat(h265Encoders));
                    audioCodecs = FilterEncoders(audioCodecs, aacEnc.Concat(mp3Enc));
                    break;

                case "webm":
                    videoCodecs = FilterEncoders(videoCodecs, vpEncoders.Concat(av1Encoders));
                    audioCodecs = FilterEncoders(audioCodecs, opusEnc.Concat(vorbisEnc));
                    break;

                case "flv":
                    videoCodecs = FilterEncoders(videoCodecs, h264Encoders.Concat(flvEncoders));
                    audioCodecs = FilterEncoders(audioCodecs, mp3Enc.Concat(aacEnc));
                    break;

                case "wmv":
                    // WMV nativo si está, si no, fallback a H.264 / MPEG4
                    videoCodecs = FilterEncoders(videoCodecs, new[] { "wmv" }.Concat(h264Encoders).Concat(mpeg4Enc));
                    audioCodecs = FilterEncoders(audioCodecs, wmaEnc.Concat(mp3Enc).Concat(aacEnc));
                    break;

                case "3gp":
                    videoCodecs = FilterEncoders(videoCodecs, h263Enc.Concat(h264Encoders).Concat(mpeg4Enc));
                    audioCodecs = FilterEncoders(audioCodecs, aacEnc.Concat(amrEnc));
                    break;

                case "mkv":
                case "avi":
                    // Muy flexibles → no filtramos nada
                    audioCodecs = OrderAudioCodecsByFormat(_allAudioCodecs.ToList(), "avi");
                    break;

                case "mov":
                    videoCodecs = FilterEncoders(videoCodecs, 
                        dnxhrEnc.Concat(h264Encoders).Concat(h265Encoders));
    
                    audioCodecs = FilterEncoders(audioCodecs, pcmEnc.Concat(aacEnc));
                    audioCodecs = OrderAudioCodecsByFormat(audioCodecs, "mov"); // NUEVO (usa el mismo método)
                    break;
            }

            return (videoCodecs, audioCodecs);
        }

        
        /// <summary>
        /// Ordena los códecs poniendo los prioritarios primero
        /// </summary>
        private List<string> OrderAudioCodecsByFormat(List<string> codecs, string? format)
        {
            if (string.IsNullOrEmpty(format))
                return codecs;
    
            switch (format.ToLower())
            {
                case "mov":
                    return codecs.OrderByDescending(c =>
                    {
                        if (c.Contains("pcm_s24le")) return 100;
                        if (c.Contains("pcm_s16le")) return 99;
                        if (c.Contains("pcm_f32le")) return 98;
                        if (c == "aac") return 50;
                        return 0;
                    }).ToList();
        
                case "avi":
                    return codecs.OrderByDescending(c =>
                    {
                        if (c == "aac") return 100;
                        if (c == "libmp3lame") return 90;
                        if (c.Contains("pcm_s24le")) return 80;
                        if (c.Contains("pcm_s16le")) return 70;
                        if (c.Contains("pcm_f32le")) return 60;
                        if (c == "flac") return 50;
                        return 0;
                    }).ToList();
        
                default:
                    return codecs;
            }
        }
        
        /// <summary>
        /// Verifica si un códec de video y audio son compatibles entre sí
        /// </summary>
        private bool IsVideoAudioCompatible(string? videoCodec, string? audioCodec)
        {
            if (string.IsNullOrEmpty(videoCodec) || string.IsNullOrEmpty(audioCodec))
                return true;
            
            // FLAC es compatible con casi todo EXCEPTO VP8/VP9 (WebM no lo soporta)
            if (audioCodec == "flac")
            {
                return !(videoCodec.Contains("libvpx") || videoCodec.Contains("vp8") || videoCodec.Contains("vp9"));
            }
            
            // VP8/VP9 SOLO funcionan con Vorbis/Opus (estándar WebM)
            if (videoCodec.Contains("libvpx") || videoCodec.Contains("vp8") || videoCodec.Contains("vp9"))
            {
                return audioCodec == "libvorbis" || audioCodec == "libopus";
            }
            
            // Vorbis/Opus SOLO funcionan con VP8/VP9 (o códecs flexibles en MKV)
            if (audioCodec == "libvorbis" || audioCodec == "libopus")
            {
                // Permitir con VP8/VP9 o cualquier códec que no sea H.264/H.265 en formato restrictivo
                return videoCodec.Contains("libvpx") || videoCodec.Contains("vp8") || videoCodec.Contains("vp9") ||
                       !(videoCodec.Contains("libx264") || videoCodec.Contains("libx265") || 
                         videoCodec.Contains("h264") || videoCodec.Contains("h265") || 
                         videoCodec.Contains("hevc"));
            }
            
            // AAC/MP3 son más flexibles pero no van bien con VP8/VP9
            if (audioCodec == "aac" || audioCodec == "libmp3lame")
            {
                return !(videoCodec.Contains("libvpx") || videoCodec.Contains("vp8") || videoCodec.Contains("vp9"));
            }
            
            // Otros códecs son flexibles
            return true;
        }
        
        /// <summary>
        /// Actualiza una colección observable solo si hay cambios reales
        /// </summary>
        private void UpdateCollectionIfNeeded(
            System.Collections.ObjectModel.ObservableCollection<string> collection, 
            System.Collections.Generic.List<string> newItems)
        {
            // Verificar si hay cambios reales
            if (collection.Count == newItems.Count && 
                collection.SequenceEqual(newItems))
                return;
            
            // Actualizar colección
            collection.Clear();
            foreach (var item in newItems)
            {
                collection.Add(item);
            }
        }
        
        /// <summary>
        /// Asegura que la selección actual sea válida, o elige la primera opción disponible
        /// </summary>
        private void EnsureValidSelection(
            System.Collections.ObjectModel.ObservableCollection<string> availableItems,
            ref string? currentSelection,
            string propertyName)
        {
            if (availableItems.Count == 0)
            {
                if (currentSelection != null)
                {
                    currentSelection = null;
                    this.RaisePropertyChanged(propertyName);
                }
                return;
            }
            
            // Si la selección actual no está disponible, elegir la primera
            if (!availableItems.Contains(currentSelection))
            {
                currentSelection = availableItems.First();
                this.RaisePropertyChanged(propertyName);
            }
        }

        private async Task ConvertVideo()
        {
            HideFileReadyButton();
            _cts = new CancellationTokenSource();

            if (VideoPaths.Count == 0)
            {
                Status = L["VConvert.Fields.NoFiles"];
                return;
            }
            
            using var effectivePause = CreateEffectivePauseToken();

            try
            {
                IFFmpegProcessor processor = new FFmpegProcessor();
                IsConverting = true;
                IsOperationRunning = true;

                int totalFiles = VideoPaths.Count;
                int currentFileIndex = 0;
                int successCount = 0;

                var videosCopy = VideoPaths.ToList();
                foreach (var video in videosCopy)
                {
                    currentFileIndex++;

                    Status = string.Format(
                        L["VConvert.Fields.Converting"],
                        currentFileIndex,
                        totalFiles,
                        Path.GetFileName(video)
                    );

                    var progress = new Progress<IFFmpegProcessor.ProgressInfo>(p =>
                    {
                        double globalProgress = ((currentFileIndex - 1) + p.Progress) / totalFiles;
                        Progress = (int)(globalProgress * 100);
                        RemainingTime = p.Remaining.ToString(@"mm\:ss");
                    });

                    string outputPath = OutputPathBuilder.GetConvertOutputPath(video, SelectedFormat?.Extension!);

                    var result = await processor.ConvertAsync(
                        video,
                        outputPath,
                        SelectedVideoCodec,
                        SelectedAudioCodec,
                        SelectedFormat?.Extension!,
                        progress,
                        _cts.Token,
                        effectivePause.Token
                    );

                    if (!result.Success)
                    {
                        _ = SoundManager.Play("fail.wav");
                        Status = string.Format(
                            L["VConvert.Fields.Error"],
                            Path.GetFileName(video),
                            result.Message
                        );
                        break;
                    }

                    successCount++;
                    bool isLast = currentFileIndex == totalFiles;
                    _ = SoundManager.Play(isLast ? "sucess-final.wav" : "sucess-partial.wav");
                    SetLastCompressedFile(result.OutputPath);

                    NotificationService notificationService = new NotificationService();
                    notificationService.ShowFileConvertedNotification(
                        string.Format(L["VConvert.Fields.NotificationMessage"], result.Message),
                        result.OutputPath
                    );
                }

                Progress = 100;

                if (successCount == totalFiles)
                {
                    Status = string.Format(
                        L["VConvert.Fields.CompletedAll"],
                        successCount,
                        successCount > 1 ? "s" : "",
                        successCount > 1 ? "s" : ""
                    );
                }
                else
                {
                    Status = string.Format(
                        L["VConvert.Fields.CompletedPartial"],
                        successCount,
                        totalFiles
                    );
                }

                IsConverting = false;
                IsOperationRunning = false;
                IsVideoPathSet = false;
            }
            catch (OperationCanceledException)
            {
                _ = SoundManager.Play("fail.wav");
                Status = L["VConvert.Fields.Canceled"];
                Progress = 0;
                IsConverting = false;
                IsOperationRunning = false;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}