using Avalonia.Controls;
using ReactiveUI;
using Avalonia.ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VManager.Behaviours.X11DragDrop;
using VManager.Services;
using VManager.Services.Core;
using VManager.Views;
using VManager.Services.Core.Execution;

namespace VManager.ViewModels;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public abstract class ViewModelBase : ReactiveObject
{
    private string _videoPath = "";
    private string _outputPath = "";
    private string _lastCompressedFilePath = "";
    private int _progress;
    private string _status = "";
    private string _warning = "";
    private bool _isFileReadyVisible;
    private bool _isClicked;
    private bool _isOperationRunning;
    private bool _isDialogVisible;
    private string _remainingTime = "00:00";
    
    private readonly PauseTokenSource _toolPauseSource = new();

    /// <summary>
    /// Token de pausa específico de esta herramienta. Combinar con GlobalPauseService.Instance.Token
    /// al invocar ExecuteAsync de la Operation correspondiente.
    /// </summary>
    public PauseToken ToolPauseToken => _toolPauseSource.Token;

    /// <summary>
    /// Combina el token global (afecta a todas las herramientas) con el propio de esta instancia.
    /// Usar esto como pauseToken al llamar a CompressOperation/ConvertOperation/etc.
    /// Debe disposearse al terminar la operación (usar 'using').
    /// </summary>
    protected CombinedPauseToken CreateEffectivePauseToken()
        => new CombinedPauseToken(GlobalPauseService.Instance.Token, ToolPauseToken);
    
    public bool IsDialogVisible
    {
        get => _isDialogVisible;
        set => this.RaiseAndSetIfChanged(ref _isDialogVisible, value);
    }
    public bool IsOperationRunning
    {
        get => _isOperationRunning;
        set
        {
            this.RaiseAndSetIfChanged(ref _isOperationRunning, value);
            this.RaisePropertyChanged(nameof(ShouldShowRemainingTime));
        }
        
    }
    
    public string VideoPath
    {
        get => _videoPath;
        set => this.RaiseAndSetIfChanged(ref _videoPath, value);
    }
    
    // Nueva lista de archivos
    private ObservableCollection<string> _videoPaths = new();
    public ObservableCollection<string> VideoPaths
    {
        get => _videoPaths;
        set => this.RaiseAndSetIfChanged(ref _videoPaths, value);
    }


    public string OutputPath
    {
        get => _outputPath;
        set => this.RaiseAndSetIfChanged(ref _outputPath, value);
    }

    public int Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }
    public string RemainingTime
    {
        get => _remainingTime;
        set
        {
            if (_remainingTime != value)
            {
                _remainingTime = value;
                this.RaisePropertyChanged(nameof(RemainingTime));
            }
        }
    }
    
    public bool ShouldShowRemainingTime => IsOperationRunning && !ConfigurationService.Current.HideRemainingTime;

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string Warning
    {
        get => _warning;
        set => this.RaiseAndSetIfChanged(ref _warning, value);
    }

    public bool IsFileReadyVisible
    {
        get => _isFileReadyVisible;
        set => this.RaiseAndSetIfChanged(ref _isFileReadyVisible, value);
    }
    
    private string _urlText;
    public string UrlText
    {
        get => _urlText;
        set => this.RaiseAndSetIfChanged(ref _urlText, value);
    }
    
    protected static readonly bool IsLinux =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    
    protected bool IsLinuxPlatform => IsLinux;
    
    public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowFileInFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearInfoCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowCancelDialogCommand { get; }
    
    private DropZoneOverlay _dropZoneOverlay = new();
    public ReactiveCommand<Unit, Unit> OpenTerminalCommand { get; }
    
    public LocalizationService L => LocalizationService.Instance;
    
    protected CancellationTokenSource? _cts;
    
    private async Task OpenX11DragDropWindow()
    {
        if (!IsLinuxPlatform) return;
        
        Console.WriteLine("=== Iniciando drag & drop con overlay ===");
        
        var mainWindow = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow == null)
        {
            Console.WriteLine("No se encontró MainWindow");
            return;
        }

        var contentArea = mainWindow.FindControl<ContentControl>("ContentArea");
        if (contentArea == null)
        {
            Console.WriteLine("No se encontró ContentArea");
            return;
        }

        var currentView = contentArea.GetVisualDescendants()
            .OfType<UserControl>()
            .FirstOrDefault();

        if (currentView == null)
        {
            Console.WriteLine("No se encontró la vista renderizada");
            return;
        }

        var border = currentView.FindControl<Border>("DropZoneBorder");
        if (border == null)
        {
            Console.WriteLine("No se encontró DropZoneBorder");
            return;
        }

        Console.WriteLine("DropZoneBorder encontrado, mostrando overlay con ventana X11...");

        // Suscribirse a cambios de view para cerrar el overlay si se navega
        IDisposable? viewChangeSubscription = contentArea.GetObservable(ContentControl.ContentProperty)
            .Subscribe(_ =>
            {
                if (_dropZoneOverlay.IsActive)
                {
                    Console.WriteLine("Cambio de view detectado, cerrando overlay...");
                    _dropZoneOverlay.ForceClose();
                }
            });

        try
        {
            string? droppedFile = await _dropZoneOverlay.ShowAsync(mainWindow, border);

            if (string.IsNullOrWhiteSpace(droppedFile))
            {
                Console.WriteLine("Path vacío o drop cancelado");
                return;
            }

            var filePaths = droppedFile.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Asignar al ViewModel
            FileAssignLogic.AssignVideoFiles(this, filePaths);
            if (filePaths.Length == 1)
            {
                VideoPath = filePaths[0];
            }

            Console.WriteLine("=== Proceso completado ===");
        }
        finally
        {
            // Limpiar suscripción
            viewChangeSubscription?.Dispose();
        }
    }
    
    protected ViewModelBase()
    {
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseVideo, outputScheduler: AvaloniaScheduler.Instance);
        ShowFileInFolderCommand = ReactiveCommand.Create(ShowFileInFolder, outputScheduler: AvaloniaScheduler.Instance);
        ClearInfoCommand = ReactiveCommand.Create(ClearInfo, outputScheduler: AvaloniaScheduler.Instance);
        ShowCancelDialogCommand = ReactiveCommand.CreateFromTask(
            () => ShowCancelDialog(), // <- Usar la versión sin retorno
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        if (IsLinuxPlatform)
        {
            OpenTerminalCommand = ReactiveCommand.CreateFromTask(
                OpenX11DragDropWindow, outputScheduler: AvaloniaScheduler.Instance);
        }
        
        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                // Dispara PropertyChanged en todo el binding que use L
                this.RaisePropertyChanged(nameof(LocalizationService));
            }
        };
        
        ConfigurationService.Current?.WhenAnyValue(x => x.HideRemainingTime)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ShouldShowRemainingTime)));
        
    }
    
    private void MostrarOverlayEnMainWindow()
    {
        // Asegurarse de que hay una ventana principal
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = desktop.MainWindow;

            if (mainWindow != null && mainWindow.DataContext is MainWindowViewModel mainVM)
            {
                mainVM.IsDialogVisible = true; // Esto activa el overlay en MainWindow
                Console.WriteLine("Se activó el overlay");
            }
        }
    }
    public async Task<bool> ShowCancelDialogInMainWindow(bool fromWindowClose = false)
    {
        MostrarOverlayEnMainWindow(); // Activa overlay
        
        // Pausar: si viene del cierre de ventana, pausamos TODO (puede haber varias
        // herramientas corriendo simultáneamente); si es ESC dentro de esta herramienta
        // puntual, pausamos solo su propio token.
        if (fromWindowClose)
            GlobalPauseService.Instance.Pause();
        else
            _toolPauseSource.Pause();

        try
        {
            // Mostrar el diálogo
            if (Application.Current != null) {
                var mainWindow = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow != null)
                {
                    var dialog = new CancelDialog { DataContext = this };
                    bool? result = await dialog.ShowDialog<bool?>(mainWindow);
            
                    if (result == true)
                    {
                        // Cancelar la operación
                        Console.WriteLine("Entrando al request cancel...");
                        RequestCancelOperation();
                    }

                    // Desactivar overlay al cerrar el dialog
                    if (mainWindow.DataContext is MainWindowViewModel mainVM)
                        mainVM.IsDialogVisible = false;
                
                    // Retornar true solo si el usuario eligió cancelar Y viene de window close
                    return result == true && fromWindowClose;
                }
            }
        
            return false;
        }
        finally
        {
            // Siempre reanudar al cerrar el diálogo (cancelar o continuar)
            if (fromWindowClose)
                GlobalPauseService.Instance.Resume();
            else
                _toolPauseSource.Resume();
        }
    }
    
    public async Task ShowCancelDialog()
    {
        await ShowCancelDialogInMainWindow(fromWindowClose: false);
    }
    
    public void RequestCancelOperation()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            Console.WriteLine("[DEBUG]: Cancelación solicitada por el usuario.");
        }
        // Método virtual que las clases derivadas pueden sobrescribir
        IsOperationRunning = false;
    }
    protected bool IsVideoPathSet { get; set; }
    public virtual void ClearInfo()
    {
        Status = L["General.Refreshed"];
        Warning = "";
        VideoPath = "";
        VideoPaths.Clear();
        Progress = 0;
        OutputPath = "";
        IsFileReadyVisible = false;
        IsVideoPathSet = false;
        IsClicked = false;
        UrlText = "";
        this.RaisePropertyChanged(nameof(Status));
        this.RaisePropertyChanged(nameof(Warning));
        this.RaisePropertyChanged(nameof(VideoPath));
        this.RaisePropertyChanged(nameof(IsFileReadyVisible));
        this.RaisePropertyChanged(nameof(Progress));
        this.RaisePropertyChanged(nameof(OutputPath));
        this.RaisePropertyChanged(nameof(IsVideoPathSet));
        this.RaisePropertyChanged(nameof(IsClicked));
        
        _ = Task.Delay(1500).ContinueWith(_ =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = "";
                this.RaisePropertyChanged(nameof(Status));
            });
        });
    }
    protected virtual bool AllowAudioFiles => false;

    private async Task BrowseVideo()
    {
        TopLevel? topLevel = null; 
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        }

        if (topLevel == null)
        {
            Status = L["Configuration.Fields.MainWindowFail"];
            this.RaisePropertyChanged(nameof(Status));
            return;
        }

        var videoPatterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.wmv", "*.flv", "*.3gp"};
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
            Title = AllowAudioFiles ? "Seleccionar videos o audios" : "Seleccionar video",
            FileTypeFilter = filters,
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            VideoPaths = new ObservableCollection<string>(
                files.Select(f => f.Path.LocalPath)
            );
            VideoPath = VideoPaths.First(); // Para la UI
            IsVideoPathSet = VideoPaths.Count > 0;

            this.RaisePropertyChanged(nameof(VideoPaths));
            this.RaisePropertyChanged(nameof(VideoPath));
            this.RaisePropertyChanged(nameof(IsVideoPathSet));
        }
    }

    
    public void SetLastCompressedFile(string path)
    {
        _lastCompressedFilePath = path;
        IsFileReadyVisible = true;
    }

    public void HideFileReadyButton()
    {
        IsFileReadyVisible = false;
        this.RaisePropertyChanged(nameof(IsFileReadyVisible));
    }
    public bool IsClicked
    {
        get => _isClicked;
        set => this.RaiseAndSetIfChanged(ref _isClicked, value);
    }

    private void ShowFileInFolder()
    {
        if (string.IsNullOrEmpty(_lastCompressedFilePath))
            return;

        try
        {
            // Abrir el explorador en la carpeta y seleccionar el archivo
            var folder = System.IO.Path.GetDirectoryName(_lastCompressedFilePath)!;
            var file = System.IO.Path.GetFileName(_lastCompressedFilePath);

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastCompressedFilePath}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux: Nautilus, Dolphin, Thunar, etc.
                System.Diagnostics.Process.Start("xdg-open", folder);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", folder);
            }

            IsClicked = true;
            this.RaisePropertyChanged(nameof(IsClicked));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al abrir carpeta: {ex.Message}");
            ErrorService.Show(ex);
        }
    }
    
}
