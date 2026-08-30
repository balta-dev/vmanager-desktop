using Avalonia.ReactiveUI;
using ReactiveUI;
using System;
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
    public class Herramienta2ViewModel : CodecViewModelBase
    {
        private bool _isConverting;
        public bool IsConverting
        {
            get => _isConverting;
            set => this.RaiseAndSetIfChanged(ref _isConverting, value);
        }

        private string _porcentajeCompresionUsuario = "75";
        public string PorcentajeCompresionUsuario
        {
            get => _porcentajeCompresionUsuario;
            set => this.RaiseAndSetIfChanged(ref _porcentajeCompresionUsuario, value);
        }

        public ReactiveCommand<Unit, Unit> CompressCommand { get; }
        
        private bool _isUpdatingCodecs = false;

        public Herramienta2ViewModel()
        {
            CompressCommand = ReactiveCommand.CreateFromTask(CompressVideos, outputScheduler: AvaloniaScheduler.Instance);
            _ = LoadCodecsAsync();
            
            // Suscripción para mantener compatibilidad entre códecs
            this.WhenAnyValue(
                x => x.SelectedVideoCodec, 
                x => x.SelectedAudioCodec)
                .Throttle(TimeSpan.FromMilliseconds(50))
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(_ => UpdateCodecCompatibility());
        }
        
        /// <summary>
        /// Actualiza la compatibilidad entre códecs de video y audio
        /// </summary>
        private void UpdateCodecCompatibility()
        {
            if (_isUpdatingCodecs || _allVideoCodecs == null || _allAudioCodecs == null) return;
            
            _isUpdatingCodecs = true;
            
            try
            {
                // Filtrar códecs de video compatibles con el audio seleccionado
                var compatibleVideoCodecs = _allVideoCodecs
                    .Where(v => IsVideoAudioCompatible(v, SelectedAudioCodec))
                    .ToList();
                
                // Filtrar códecs de audio compatibles con el video seleccionado
                var compatibleAudioCodecs = _allAudioCodecs
                    .Where(a => IsVideoAudioCompatible(SelectedVideoCodec, a))
                    .ToList();
                
                // Actualizar listas solo si hay cambios reales
                UpdateCollectionIfNeeded(AvailableVideoCodecs, compatibleVideoCodecs);
                UpdateCollectionIfNeeded(AvailableAudioCodecs, compatibleAudioCodecs);
                
                // Validar y ajustar selecciones si es necesario
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
        /// Verifica si un códec de video y audio son compatibles entre sí
        /// </summary>
        private bool IsVideoAudioCompatible(string? videoCodec, string? audioCodec)
        {
            if (string.IsNullOrEmpty(videoCodec) || string.IsNullOrEmpty(audioCodec))
                return true;
            
            // NUEVO: DNxHR funciona mejor con PCM
            if (videoCodec.Contains("dnxhd") || videoCodec.Contains("dnxhr"))
            {
                // DNxHR acepta PCM (ideal) o AAC
                return audioCodec.Contains("pcm") || audioCodec == "aac";
            }
    
            // PCM funciona con casi todo excepto WebM
            if (audioCodec.Contains("pcm"))
            {
                return !(videoCodec.Contains("libvpx") || videoCodec.Contains("vp8") || videoCodec.Contains("vp9"));
            }
            
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

        private async Task CompressVideos()
        {
            HideFileReadyButton();
            _cts = new CancellationTokenSource();

            if (!int.TryParse(PorcentajeCompresionUsuario, out int percentValue) ||
                percentValue <= 0 || percentValue > 100)
            {
                Status = L["VCompress.Fields.InvalidPercent"];
                return;
            }

            if (VideoPaths.Count == 0)
            {
                Status = L["VCompress.Fields.NoFiles"];
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
                        L["VCompress.Fields.Compressing"],
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

                    string outputPath = OutputPathBuilder.GetCompressOutputPath(video, percentValue);

                    var result = await processor.CompressAsync(
                        video,
                        outputPath,
                        percentValue,
                        SelectedVideoCodec,
                        SelectedAudioCodec,
                        progress,
                        _cts.Token,
                        effectivePause.Token
                    );

                    if (!result.Success)
                    {
                        _ = SoundManager.Play("fail.wav");
                        Status = string.Format(
                            L["VCompress.Fields.Error"],
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
                        string.Format(L["VCompress.Fields.NotificationMessage"], result.Message),
                        result.OutputPath
                    );
                }

                Progress = 100;

                Status = successCount == totalFiles
                    ? string.Format(
                        L["VCompress.Fields.CompletedAll"],
                        successCount,
                        successCount > 1 ? "s" : "",
                        successCount > 1 ? "s" : ""
                    )
                    : string.Format(
                        L["VCompress.Fields.CompletedPartial"],
                        successCount,
                        totalFiles
                    );

                IsConverting = false;
                IsOperationRunning = false;
                IsVideoPathSet = false;
            }
            catch (OperationCanceledException)
            {
                _ = SoundManager.Play("fail.wav");
                Status = L["VCompress.Fields.Canceled"];
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