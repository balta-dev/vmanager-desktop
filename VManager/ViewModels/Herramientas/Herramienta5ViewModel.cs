using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.ReactiveUI;
using ReactiveUI;
using VManager.Messages;
using VManager.Models;
using VManager.Services;
using VManager.Services.Core;
using VManager.Services.Models;

namespace VManager.ViewModels.Herramientas
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public class Herramienta5ViewModel : CodecViewModelBase
    {
        private static async Task<YtDlpProcessor> CreateYtDlpProcessorAsync()
        {
            await YtDlpManager.EnsureInitialized();
            await DenoManager.EnsureInitialized();
            return new YtDlpProcessor();
        }

        // ── Formatos fijos para items de playlist ─────────────────
        private static readonly ObservableCollection<VManager.Models.VideoFormat> PlaylistFormats = new()
        {
            new VManager.Models.VideoFormat { FormatId = "best", Resolution = "Mejor calidad", Extension = "mp4" },
            new VManager.Models.VideoFormat { FormatId = "0",    Resolution = "audio",          Extension = "mp3" },
            new VManager.Models.VideoFormat { FormatId = "1",    Resolution = "audio",          Extension = "wav" },
        };
        // ─────────────────────────────────────────────────────────

        private ObservableCollection<VideoDownloadItem> _videos = new();
        public ObservableCollection<VideoDownloadItem> Videos
        {
            get => _videos;
            set => this.RaiseAndSetIfChanged(ref _videos, value);
        }

        private string _urlText = string.Empty;
        public string UrlText
        {
            get => _urlText;
            set => this.RaiseAndSetIfChanged(ref _urlText, value);
        }

        private readonly HttpClient _httpClient = new();

        public AudioFormat SelectedAudioFormat { get; set; }
        
        private bool _isConverting;
        public bool IsConverting
        {
            get => _isConverting;
            set => this.RaiseAndSetIfChanged(ref _isConverting, value);
        }
        
        private VideoDownloadItem? _selectedVideo;
        public VideoDownloadItem? SelectedVideo
        {
            get => _selectedVideo;
            set => this.RaiseAndSetIfChanged(ref _selectedVideo, value);
        }
        
        private bool _showDownloadHelp;
        public bool ShowDownloadHelp
        {
            get => _showDownloadHelp;
            set => this.RaiseAndSetIfChanged(ref _showDownloadHelp, value);
        }
        
        public override void ClearInfo()
        {
            base.ClearInfo();
            SelectedVideo = null;
            this.RaisePropertyChanged(nameof(SelectedVideo));
        }
        
        private readonly AppConfig _config;

        public ReactiveCommand<Unit, Unit> DownloadCommand { get; }
        public ReactiveCommand<string, Unit> AddUrlCommand { get; }
        public ReactiveCommand<VideoDownloadItem, Unit> RemoveUrlCommand { get; }
        public ReactiveCommand<Unit, Unit> HideDownloadHelpCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearAllCommand { get; }
        public ReactiveCommand<Unit, Unit> GoToConfigCommand { get; }

        private readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3, 3);

        public Herramienta5ViewModel()
        {
            _config = ConfigurationService.Current;
            DownloadCommand = ReactiveCommand.CreateFromTask(DownloadVideos, outputScheduler: AvaloniaScheduler.Instance);
            AddUrlCommand = ReactiveCommand.Create<string>(AddUrl, outputScheduler: AvaloniaScheduler.Instance);
            RemoveUrlCommand = ReactiveCommand.Create<VideoDownloadItem>(RemoveUrl, outputScheduler: AvaloniaScheduler.Instance);
            HideDownloadHelpCommand = ReactiveCommand.Create(
                () => { ShowDownloadHelp = false; },
                outputScheduler: AvaloniaScheduler.Instance
            );
            GoToConfigCommand = ReactiveCommand.Create(() =>
            {
                MessageBus.Current.SendMessage(new NavigateToConfigAndScrollMessage());
            }, outputScheduler: AvaloniaScheduler.Instance);
            ClearAllCommand = ReactiveCommand.Create(ClearAll, outputScheduler: AvaloniaScheduler.Instance);
            SelectedAudioFormat = SupportedAudioFormats[0]; // mp3
        }

        public bool IsVideoPathSet
        {
            get => Videos.Count > 0;
            set { /* requerido por la clase base */ }
        }

        public int VideoCount => Videos.Count;

        private bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        // ============================================================
        //              CLASIFICACIÓN DE URL
        // ============================================================

        private enum UrlType { Single, VideoInPlaylist, Playlist }

        /// <summary>
        /// Clasifica la URL en base a los query params.
        /// Solo se usa para YouTube; otras plataformas se manejan vía _type del JSON de yt-dlp.
        /// </summary>
        private static UrlType ClassifyYouTubeUrl(Uri uri, out string cleanUrl)
        {
            var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
            bool hasList = !string.IsNullOrEmpty(qs["list"]);
            bool hasV    = !string.IsNullOrEmpty(qs["v"]);

            if (hasList && hasV)
            {
                // Video dentro de una playlist → stripear list e index
                var builder = new UriBuilder(uri)
                {
                    Query = $"v={qs["v"]}"
                };
                cleanUrl = builder.Uri.ToString();
                return UrlType.VideoInPlaylist;
            }

            if (hasList && !hasV)
            {
                cleanUrl = uri.ToString();
                return UrlType.Playlist;
            }

            cleanUrl = uri.ToString();
            return UrlType.Single;
        }

        // ============================================================
        //              AGREGAR URL
        // ============================================================

        public async void AddUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Status = L["VideoStatus.NoVideo"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            if (!IsValidUrl(url))
            {
                Status = L["VideoStatus.NotURL"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;

            UrlText = string.Empty;
            bool isYouTube = uri.Host.Contains("youtube.com") || uri.Host.Contains("youtu.be");

            // --- 1. CASO YOUTUBE (Ya optimizado) ---
            if (isYouTube)
            {
                var urlType = ClassifyYouTubeUrl(uri, out string cleanUrl);

                if (urlType == UrlType.VideoInPlaylist)
                {
                    Status = L["VideoStatus.PlaylistIgnored"];
                    this.RaisePropertyChanged(nameof(Status));
                    await AddSingleVideoAsync(cleanUrl);
                    return;
                }

                if (urlType == UrlType.Playlist)
                {
                    await AddPlaylistAsync(url);
                    return;
                }
        
                // Es un video normal de YT
                await AddSingleVideoAsync(url);
                return;
            }

            // --- 2. OTRAS PLATAFORMAS (Instagram, TikTok, Twitter, etc.) ---
            // En lugar de ir directo a AddPlaylistAsync, chequeamos si la URL 
            // contiene keywords típicas de listas.
            bool looksLikePlaylist = url.Contains("playlist") || 
                                     url.Contains("album") || 
                                     url.Contains("/sets/") || 
                                     url.Contains("series");

            if (looksLikePlaylist)
            {
                await AddPlaylistAsync(url);
            }
            else
            {
                // El 99% de los links de redes sociales van por acá ahora (INSTANTÁNEO)
                await AddSingleVideoAsync(url);
            }
        }

        /// <summary>
        /// Agrega un video suelto (flujo original).
        /// </summary>
        private async Task AddSingleVideoAsync(string url)
        {
            if (Videos.Any(v => v.Url == url))
            {
                Status = L["VideoStatus.AlreadyOnList"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            var videoItem = new VideoDownloadItem
            {
                Url = url,
                Title = L["VideoStatus.RetrievingInfo"],
                IsLoading = true
            };

            Videos.Add(videoItem);
            this.RaisePropertyChanged(nameof(IsVideoPathSet));
            this.RaisePropertyChanged(nameof(VideoCount));

            await LoadVideoInfoAsync(videoItem);
        }

        /// <summary>
        /// Detecta si la URL es una playlist (vía yt-dlp --flat-playlist -J).
        /// Si lo es, agrega todos sus videos. Si no, la trata como video suelto.
        /// </summary>
        private async Task AddPlaylistAsync(string url)
        {
            // Item placeholder mientras detectamos
            var detectingItem = new VideoDownloadItem
            {
                Url = url,
                Title = L["VideoStatus.DetectingPlaylist"], // "Detectando..."
                IsLoading = true
            };
            Videos.Add(detectingItem);
            this.RaisePropertyChanged(nameof(IsVideoPathSet));
            this.RaisePropertyChanged(nameof(VideoCount));

            try
            {
                var processor = await CreateYtDlpProcessorAsync();
                var playlistInfo = await processor.GetPlaylistInfoAsync(url);

                // Quitar el placeholder
                Videos.Remove(detectingItem);

                if (playlistInfo == null || !playlistInfo.IsPlaylist)
                {
                    // No es playlist → flujo normal
                    await AddSingleVideoAsync(url);
                    return;
                }

                // ── Es una playlist ───────────────────────────────
                var entries = playlistInfo.Entries;
                if (entries == null || entries.Count == 0)
                {
                    Status = L["VideoStatus.PlaylistEmpty"]; // "La playlist no tiene videos."
                    this.RaisePropertyChanged(nameof(Status));
                    return;
                }

                string playlistId = playlistInfo.Id ?? url;
                Status = $"Cargando playlist: 0/{entries.Count}";
                this.RaisePropertyChanged(nameof(Status));

                // Semáforo para no cargar thumbnails de 100 videos en paralelo
                using var thumbSemaphore = new SemaphoreSlim(4, 4);
                int loaded = 0;

                var tasks = entries.Select(async entry =>
                {
                    string entryUrl = entry.EffectiveUrl ?? url;

                    // Deduplicar
                    if (Videos.Any(v => v.Url == entryUrl))
                        return;

                    var item = new VideoDownloadItem
                    {
                        Url = entryUrl,
                        Title = entry.Title ?? "...",
                        Duration = entry.Duration.HasValue ? FormatDuration(entry.Duration.Value) : string.Empty,
                        PlaylistId = playlistId,
                        IsLoading = false,
                        AvailableFormats = PlaylistFormats,
                        SelectedFormat = PlaylistFormats[0] // "Mejor calidad"
                    };

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Videos.Add(item);
                        this.RaisePropertyChanged(nameof(IsVideoPathSet));
                        this.RaisePropertyChanged(nameof(VideoCount));
                    });

                    // Cargar thumbnail en background con semáforo
                    string? thumbUrl = entry.BestThumbnailUrl;
                    if (!string.IsNullOrEmpty(thumbUrl))
                    {
                        await thumbSemaphore.WaitAsync();
                        try
                        {
                            var bytes = await _httpClient.GetByteArrayAsync(thumbUrl);
                            using var ms = new MemoryStream(bytes);
                            item.ThumbnailBitmap = new Bitmap(ms);
                        }
                        catch { /* thumbnail no crítico */ }
                        finally
                        {
                            thumbSemaphore.Release();
                        }
                    }

                    int n = Interlocked.Increment(ref loaded);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Status = $"Cargando playlist: {n}/{entries.Count}";
                        this.RaisePropertyChanged(nameof(Status));
                    });
                });

                await Task.WhenAll(tasks);

                Status = $"Playlist cargada: {entries.Count} videos";
                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(VideoCount));
            }
            catch (Exception ex)
            {
                Videos.Remove(detectingItem);
                Console.WriteLine($"[PLAYLIST] Error: {ex}");
                ErrorService.Show(ex);

                // Fallback: intentar como video suelto
                await AddSingleVideoAsync(url);
            }
        }

        // ============================================================
        //          SINCRONIZACIÓN DE FORMATO EN PLAYLIST
        // ============================================================

        /// <summary>
        /// Cuando el usuario cambia el formato de un item de playlist,
        /// propaga el cambio a todos los demás items de la misma playlist
        /// que no hayan sido modificados manualmente.
        /// Llamar desde la View o mediante suscripción a PropertyChanged.
        /// </summary>
        public void OnPlaylistItemFormatChanged(VideoDownloadItem changedItem)
        {
            if (changedItem.PlaylistId == null || changedItem.SelectedFormat == null)
                return;

            changedItem.FormatOverriddenByUser = true;

            var videosCopy = Videos.ToList();
            foreach (var item in videosCopy)
            {
                if (item == changedItem) continue;
                if (item.PlaylistId != changedItem.PlaylistId) continue;
                if (item.FormatOverriddenByUser) continue;

                item.SelectedFormat = changedItem.SelectedFormat;
            }
        }

        // ============================================================
        //                  CARGAR INFO DE VIDEO INDIVIDUAL
        // ============================================================

        private async Task LoadVideoInfoAsync(VideoDownloadItem videoItem)
        {
            try
            {
                var processor = await CreateYtDlpProcessorAsync();
                var (info, cookiesProblem, usedCookies) =
                    await processor.GetVideoInfoWithDetectionAsync(videoItem.Url);
                
                videoItem.UsedCookies = usedCookies;

                if (cookiesProblem)
                {
                    videoItem.HasError = true;
                    videoItem.Title = L["VideoStatus.ErrorNoInfo"];
                    videoItem.IsLoading = false;
                    ShowDownloadHelp = true;
                    ErrorService.Show(L["Errors.CookiesExpired"], null, L["Errors.CookiesWarningTitle"], "#FFFFA500");
                }

                if (info == null)
                {
                    videoItem.HasError = true;
                    videoItem.ErrorMessage = L["VideoStatus.ErrorNoInfo"];
                    videoItem.Title = L["VideoStatus.ErrorNoInfo"];
                    videoItem.IsLoading = false;
                    ShowDownloadHelp = true;
                    return;
                }

                videoItem.Title = info.Title;
                videoItem.Duration = FormatDuration(info.Duration);
                videoItem.ThumbnailUrl = info.Thumbnail;
                videoItem.FileSize = info.FileSize;
                
                var seenResolutions = new HashSet<string>();
                var formatList = new List<VManager.Models.VideoFormat>();

                foreach (var f in info.Formats
                             .Where(fmt => !string.IsNullOrEmpty(fmt.VideoCodec) && 
                                           fmt.VideoCodec != "none" && 
                                           fmt.Height.HasValue)
                             .OrderByDescending(fmt => fmt.Height))
                {
                    string resolution = $"{f.Height}p";
    
                    if (seenResolutions.Add(resolution))
                    {
                        formatList.Add(new VManager.Models.VideoFormat
                        {
                            FormatId = f.FormatId,
                            Resolution = resolution,
                            Extension = f.Extension,
                            FileSize = f.FileSize
                        });
                    }
                }
                
                formatList.Add(new VManager.Models.VideoFormat
                {
                    FormatId = "0",
                    Resolution = "audio",
                    Extension = "mp3",
                    FileSize = null
                });
                
                formatList.Add(new VManager.Models.VideoFormat
                {
                    FormatId = "1",
                    Resolution = "audio",
                    Extension = "wav",
                    FileSize = null
                });
                
                videoItem.AvailableFormats = new ObservableCollection<VManager.Models.VideoFormat>(formatList);
                videoItem.SelectedFormat = videoItem.AvailableFormats.FirstOrDefault();

                if (!string.IsNullOrEmpty(info.Thumbnail))
                {
                    try
                    {
                        var thumbnailBytes = await _httpClient.GetByteArrayAsync(info.Thumbnail);
                        using var ms = new MemoryStream(thumbnailBytes);
                        videoItem.ThumbnailBitmap = new Bitmap(ms);
                    }
                    catch { }
                }

                videoItem.IsLoading = false;
            }
            catch (Exception ex)
            {
                videoItem.HasError = true;
                videoItem.ErrorMessage = ex.Message;
                videoItem.Title = L["VideoStatus.ErrorNoInfo"];
                videoItem.IsLoading = false;
                Console.WriteLine($"Error cargando info: {ex}");
                ErrorService.Show(ex);
            }
        }

        private string FormatDuration(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return ts.ToString(@"h\:mm\:ss");
            return ts.ToString(@"m\:ss");
        }

        // ============================================================
        //                  LIMPIAR COLA
        // ============================================================

        private void ClearAll()
        {
            var downloading = Videos.Any(v => v.IsDownloading);
            if (downloading)
            {
                Status = L["VideoStatus.CantDeleteWhileDownloading"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            Videos.Clear();
            this.RaisePropertyChanged(nameof(IsVideoPathSet));
            this.RaisePropertyChanged(nameof(VideoCount));
        }

        public void RemoveUrl(VideoDownloadItem video)
        {
            if (video.IsDownloading)
            {
                Status = L["VideoStatus.CantDeleteWhileDownloading"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }
            
            video.IsCanceled = true;
            Videos.Remove(video);
            this.RaisePropertyChanged(nameof(IsVideoPathSet));
            this.RaisePropertyChanged(nameof(VideoCount));
        }

        protected override bool AllowAudioFiles => false;
        
        private readonly Dictionary<VideoDownloadItem, double> _maxProgress = new();
        private readonly Dictionary<VideoDownloadItem, double> _realProgress = new();
        
        // ============================================================
        //                  DESCARGAR VIDEOS
        // ============================================================

        private async Task DownloadVideos()
        {
            if (Videos.Count == 0)
            {
                Status = L["VideoStatus.NoVideo"];
                this.RaisePropertyChanged(nameof(Status));
                return;
            }

            HideFileReadyButton();
            _cts = new CancellationTokenSource();
            Progress = 0;
            this.RaisePropertyChanged(nameof(Progress));

            try
            {
                var processor = await CreateYtDlpProcessorAsync();

                IsConverting = true;
                IsOperationRunning = true;

                var pendingVideos = Videos.Where(v => !v.IsCompleted && !v.IsCanceled).ToList();
                int total = pendingVideos.Count;

                if (total == 0)
                {
                    Status = L["VideoStatus.NoVideo"];
                    this.RaisePropertyChanged(nameof(Status));
                    return;
                }

                int successCount = 0;
                int finishedCount = 0;
                var errorMessages = new List<string>();
                string lastGlobalETA = "";
                object lockObj = new object();

                Status = $"{L["VideoStatus.Completed"]} 0/{total} {L["VideoStatus.Downloads"]}";
                this.RaisePropertyChanged(nameof(Status));

                foreach (var v in pendingVideos)
                {
                    _realProgress[v] = 0;
                    _maxProgress[v] = 0;
                }

                var downloadTasks = pendingVideos.Select(async (currentVideo) =>
                {
                    await _downloadSemaphore.WaitAsync(_cts.Token);

                    try
                    {
                        currentVideo.IsDownloading = true;
                        currentVideo.Progress = 0;

                        if (total == 1)
                            currentVideo.Status = L["VideoStatus.Downloading"];

                        var progress = new Progress<YtDlpProgress>(p =>
                        {
                            double newValue = p.Progress * 100;

                            lock (lockObj)
                            {
                                if (!_maxProgress.ContainsKey(currentVideo))
                                    _maxProgress[currentVideo] = 0;

                                if (newValue >= 100 && _maxProgress[currentVideo] < 5)
                                    return;

                                if (newValue < _maxProgress[currentVideo])
                                    newValue = _maxProgress[currentVideo];
                                else
                                    _maxProgress[currentVideo] = newValue;

                                _realProgress[currentVideo] = newValue;

                                if (!string.IsNullOrWhiteSpace(p.Eta) && 
                                    !p.Eta.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                                {
                                    lastGlobalETA = p.Eta;
                                }
                            }

                            currentVideo.Progress = newValue;

                            if (total == 1)
                            {
                                currentVideo.Status = $"{p.Speed} - ETA: {p.Eta}";
                            }
                            else
                            {
                                string etaPart = !string.IsNullOrWhiteSpace(p.Eta) ? $" - ETA: {p.Eta}" : "";
                                currentVideo.Status = $"{newValue:F0}%{etaPart}";
                            }

                            double globalProgress = pendingVideos.Count > 0
                                ? pendingVideos.Average(v => _realProgress.GetValueOrDefault(v, 0))
                                : 0;

                            Progress = (int)globalProgress;
                            RemainingTime = lastGlobalETA;

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                this.RaisePropertyChanged(nameof(Progress));
                                this.RaisePropertyChanged(nameof(RemainingTime));
                                this.RaisePropertyChanged(nameof(Status));
                            });
                        });

                        string downloadFolder = !string.IsNullOrWhiteSpace(_config.PreferredDownloadFolder)
                            ? _config.PreferredDownloadFolder
                            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                        // ── Determinar formatId y extensión ───────────────
                        string? formatId = currentVideo.SelectedFormat?.FormatId;
                        string extension = formatId switch
                        {
                            "0"    => "mp3",
                            "1"    => "wav",
                            "best" => "mp4",
                            _      => "mp4"
                        };

                        // Para el formatId que se pasa a yt-dlp:
                        // "best" → bestvideo+bestaudio/best (se resuelve en DownloadAsync)
                        // null  → yt-dlp elige por defecto
                        string? ytFormatId = formatId == "best" ? null : formatId;

                        string safeTitle = currentVideo.Title
                            .Replace("/", "-")
                            .Replace("\\", "-")
                            .Replace("\"", "'");
                        string outputTemplate = Path.Combine(downloadFolder, $"{safeTitle}.{extension}");

                        var result = await processor.DownloadAsync(
                            currentVideo.Url,
                            outputTemplate,
                            progress,
                            _cts.Token,
                            ytFormatId,
                            currentVideo.UsedCookies
                        );

                        bool fileDownloaded = File.Exists(outputTemplate);

                        if (fileDownloaded || result.Success)
                        {
                            Interlocked.Increment(ref successCount);
                            currentVideo.IsCompleted = true;
                            currentVideo.Status = L["VideoStatus.Completed"];
                            currentVideo.Progress = 100;

                            var notifier = new NotificationService();
                            notifier.ShowFileConvertedNotification($"{L["VideoStatus.Downloaded"]} {currentVideo.Title}", outputTemplate);

                            if (!OperatingSystem.IsWindows())
                            {
                                // finishedCount todavía no fue incrementado acá, se incrementa en el finally
                                // así que comparamos con total - 1
                                bool isLast = (finishedCount + 1) == total;
                                _ = SoundManager.Play(isLast ? "sucess-final.wav" : "sucess-partial.wav");
                            }
                            SetLastCompressedFile(outputTemplate);
                        }
                        else
                        {
                            currentVideo.HasError = true;
                            currentVideo.Status = L["VideoStatus.Error"];
                            
                            lock (lockObj)
                            {
                                errorMessages.Add($"{currentVideo.Title}: {result.Message ?? "Error desconocido"}");
                            }
                            
                            _ = SoundManager.Play("fail.wav");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        currentVideo.IsCanceled = true;
                        currentVideo.Status = L["VideoStatus.Canceled"];
                        currentVideo.Progress = 0;
                    }
                    catch (Exception ex)
                    {
                        currentVideo.HasError = true;
                        currentVideo.Status = L["VideoStatus.Error"];
                        
                        lock (lockObj)
                        {
                            errorMessages.Add($"{currentVideo.Title}: {ex.Message}");
                        }
                        
                        _ = SoundManager.Play("fail.wav");
                        ErrorService.Show(ex);
                    }
                    finally
                    {
                        currentVideo.IsDownloading = false;
                        _downloadSemaphore.Release();

                        Interlocked.Increment(ref finishedCount);

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            Status = $"{L["VideoStatus.Completed"]} {finishedCount}/{total} {L["VideoStatus.Downloads"]}";
                            
                            lock (lockObj)
                            {
                                if (errorMessages.Count > 0)
                                    Status += $" ({errorMessages.Count} con problemas)";
                            }

                            this.RaisePropertyChanged(nameof(Progress));
                            this.RaisePropertyChanged(nameof(Status));
                        });
                    }
                }).ToList();

                await Task.WhenAll(downloadTasks);

                Progress = 100;
                RemainingTime = "";

                string finalStatus = $"{L["VideoStatus.Completed"]} {finishedCount}/{total} {L["VideoStatus.Downloads"]}";
                
                lock (lockObj)
                {
                    if (errorMessages.Count > 0)
                        finalStatus += $" ({errorMessages.Count} con problemas)";
                }

                Status = finalStatus;

                this.RaisePropertyChanged(nameof(Progress));
                this.RaisePropertyChanged(nameof(RemainingTime));
                this.RaisePropertyChanged(nameof(Status));
            }
            catch (OperationCanceledException)
            {
                Status = L["VideoStatus.OperationCanceled"];
                Progress = 0;
                RemainingTime = "";
                this.RaisePropertyChanged(nameof(Status));
                this.RaisePropertyChanged(nameof(Progress));
                this.RaisePropertyChanged(nameof(RemainingTime));
                _ = SoundManager.Play("fail.wav");
            }
            finally
            {
                IsConverting = false;
                IsOperationRunning = false;
                _cts?.Dispose();
                _cts = null;
                this.RaisePropertyChanged(nameof(IsConverting));
                this.RaisePropertyChanged(nameof(IsOperationRunning));
            }
        }
    }
}
