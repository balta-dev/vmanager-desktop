using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq; // <-- este es el que necesitás para .Where()
using ReactiveUI;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia;
using Avalonia.Media.Imaging;
using VManager.Messages;
using VManager.Services;
using VManager.Services.Core;
using VManager.ViewModels.Herramientas;

namespace VManager.ViewModels;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class MainWindowViewModel : ViewModelBase
{
    public bool IsWelcomeVisible => CurrentView == null;
    
    private bool _hidePane;
    public bool HidePane
    {
        get => _hidePane;
        set => this.RaiseAndSetIfChanged(ref _hidePane, value);
    }
    
    private bool _showThemeToggleButton = true;
    public bool ShowThemeToggleButton
    {
        get => _showThemeToggleButton;
        set => this.RaiseAndSetIfChanged(ref _showThemeToggleButton, value);
    }
    
    private Herramienta1ViewModel? _herramienta1;
    private Herramienta2ViewModel? _herramienta2;
    private Herramienta3ViewModel? _herramienta3;
    private Herramienta4ViewModel? _herramienta4;
    private Herramienta5ViewModel? _herramienta5;
    private Herramienta6ViewModel? _herramienta6;
    private ConfigurationViewModel? _configuration;
    private AcercaDeViewModel? _acercaDe;

    private Herramienta1ViewModel Herramienta1 => _herramienta1 ??= new Herramienta1ViewModel();
    private Herramienta2ViewModel Herramienta2 => _herramienta2 ??= new Herramienta2ViewModel();
    private Herramienta3ViewModel Herramienta3 => _herramienta3 ??= new Herramienta3ViewModel();
    private Herramienta4ViewModel Herramienta4 => _herramienta4 ??= new Herramienta4ViewModel();
    private Herramienta5ViewModel Herramienta5 => _herramienta5 ??= new Herramienta5ViewModel();
    private Herramienta6ViewModel Herramienta6 => _herramienta6 ??= new Herramienta6ViewModel();
    private AcercaDeViewModel AcercaDe => _acercaDe ??= new AcercaDeViewModel();

    public List<ViewModelBase> Tools =>
        new ViewModelBase?[] { _herramienta1, _herramienta2, _herramienta3, _herramienta4, _herramienta5, _herramienta6 }
            .Where(t => t != null)
            .Cast<ViewModelBase>()
            .ToList();
    
    private bool _herramienta1Activa;
    public bool Herramienta1Activa
    {
        get => _herramienta1Activa;
        set => this.RaiseAndSetIfChanged(ref _herramienta1Activa, value);
    }

    private bool _herramienta2Activa;
    public bool Herramienta2Activa
    {
        get => _herramienta2Activa;
        set => this.RaiseAndSetIfChanged(ref _herramienta2Activa, value);
    }
    
    private bool _herramienta3Activa;
    public bool Herramienta3Activa
    {
        get => _herramienta3Activa;
        set => this.RaiseAndSetIfChanged(ref _herramienta3Activa, value);
    }
    
    private bool _herramienta4Activa;
    public bool Herramienta4Activa
    {
        get => _herramienta4Activa;
        set => this.RaiseAndSetIfChanged(ref _herramienta4Activa, value);
    }
    
    private bool _herramienta5Activa;
    public bool Herramienta5Activa
    {
        get => _herramienta5Activa;
        set => this.RaiseAndSetIfChanged(ref _herramienta5Activa, value);
    }
    
    private bool _herramienta6Activa;
    public bool Herramienta6Activa
    {
        get => _herramienta6Activa;
        set => this.RaiseAndSetIfChanged(ref _herramienta6Activa, value);
    }
    
    private bool _configuracionActiva;
    public bool ConfiguracionActiva
    {
        get => _configuracionActiva;
        set => this.RaiseAndSetIfChanged(ref _configuracionActiva, value);
    }

    private bool _acercaDeActivo;
    public bool AcercaDeActivo
    {
        get => _acercaDeActivo;
        set => this.RaiseAndSetIfChanged(ref _acercaDeActivo, value);
    }
    
    private ViewModelBase? _currentView;
    public ViewModelBase? CurrentView
    {
        get => _currentView;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentView, value);
            this.RaisePropertyChanged(nameof(IsWelcomeVisible));
        }
    }
    
    public ReactiveCommand<Unit, Unit> GoToHerramienta1 { get; }
    public ReactiveCommand<Unit, Unit> GoToHerramienta2 { get; }
    public ReactiveCommand<Unit, Unit> GoToHerramienta3 { get; }
    public ReactiveCommand<Unit, Unit> GoToHerramienta4 { get; }
    public ReactiveCommand<Unit, Unit> GoToHerramienta5 { get; }
    public ReactiveCommand<Unit, Unit> GoToHerramienta6 { get; }
    public ReactiveCommand<Unit, Unit> GoToAcercaDe { get; }
    public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenGitHubCommand { get; }
    public ReactiveCommand<Unit, Unit> GoToConfiguration { get; }
    
    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public string WelcomeMessage => L["General.WelcomeMessage"];
    
    private bool _showCustomIcon = true;
    public bool ShowCustomIcon
    {
        get => _showCustomIcon;
        set => this.RaiseAndSetIfChanged(ref _showCustomIcon, value);
    }
    
    private Bitmap? _userImage;
    public Bitmap? UserImage
    {
        get => _userImage;
        set => this.RaiseAndSetIfChanged(ref _userImage, value);
    }
    
    public string VersionText { get; } =
        $"{Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)}";

    public ConfigurationViewModel Configuration => _configuration ??= CreateConfigurationViewModel();

    private bool _configurationSubscriptionsInitialized;

    private ConfigurationViewModel CreateConfigurationViewModel()
    {
        var vm = new ConfigurationViewModel();
        EnsureConfigurationSubscriptions(vm);
        return vm;
    }

    private void EnsureConfigurationSubscriptions(ConfigurationViewModel config)
    {
        if (_configurationSubscriptionsInitialized)
            return;

        _configurationSubscriptionsInitialized = true;

        config.WhenAnyValue(x => x.UseCustomIcon)
            .Subscribe(useCustom =>
            {
                ShowCustomIcon = useCustom;
                LoadProfileImage();
            });

        config.WhenAnyValue(x => x.ProfileImagePath)
            .Subscribe(_ => LoadProfileImage());

        config.WhenAnyValue(x => x.HidePane)
            .Subscribe(value => HidePane = value);

        config.WhenAnyValue(x => x.ShowThemeToggleButton)
            .Subscribe(value => ShowThemeToggleButton = value);
    }

    public MainWindowViewModel()
    {
        ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme, outputScheduler: AvaloniaScheduler.Instance);
        OpenGitHubCommand = ReactiveCommand.Create(OpenGitHub, outputScheduler: AvaloniaScheduler.Instance);

        var appConfig = ConfigurationService.Current;
        ConfigurationViewModel.ApplySavedLanguage(appConfig.Language);
        HidePane = appConfig.HidePane;
        ShowThemeToggleButton = appConfig.ShowThemeToggleButton;
        ShowCustomIcon = appConfig.UseCustomIcon;
        
        MessageBus.Current.Listen<NavigateToConfigAndScrollMessage>()
            .Subscribe(_ =>
            {
                var config = Configuration;
                CurrentView = config;
                config.RequestScrollToBottom?.Invoke();
            });
        
        // Las 5 herramientas ya NO se crean acá, se crean lazy al primer uso
        
        GoToHerramienta1 = ReactiveCommand.Create(
            () =>
            {
                Herramienta1Activa = true;
                Herramienta2Activa = false;
                Herramienta3Activa = false;
                Herramienta4Activa = false;
                Herramienta5Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = false;
                AcercaDeActivo = false;
                CurrentView = Herramienta1; // propiedad lazy, no campo
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );

        GoToHerramienta2 = ReactiveCommand.Create(
            () =>
            {
                Herramienta2Activa = true;
                Herramienta1Activa = false;
                Herramienta3Activa = false;
                Herramienta4Activa = false;
                Herramienta5Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = false;
                AcercaDeActivo = false;
                CurrentView = Herramienta2;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        GoToHerramienta3 = ReactiveCommand.Create(
            () =>
            {
                Herramienta3Activa = true;
                Herramienta1Activa = false;
                Herramienta2Activa = false;
                Herramienta4Activa = false;
                Herramienta5Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = false;
                AcercaDeActivo = false;
                CurrentView = Herramienta3;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        GoToHerramienta4 = ReactiveCommand.Create(
            () =>
            {
                Herramienta4Activa = true;
                Herramienta1Activa = false;
                Herramienta2Activa = false;
                Herramienta3Activa = false;
                Herramienta5Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = false;
                AcercaDeActivo = false;
                CurrentView = Herramienta4;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        GoToHerramienta5 = ReactiveCommand.Create(
            () =>
            {
                Herramienta5Activa = true;
                Herramienta1Activa = false;
                Herramienta2Activa = false;
                Herramienta3Activa = false;
                Herramienta4Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = false;
                AcercaDeActivo = false;
                CurrentView = Herramienta5;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        GoToHerramienta6 = ReactiveCommand.Create(
            () =>
            {
                Herramienta6Activa = true;
                Herramienta1Activa = false;
                Herramienta2Activa = false;
                Herramienta3Activa = false;
                Herramienta4Activa = false;
                Herramienta5Activa = false;
                ConfiguracionActiva = false;
                AcercaDeActivo = false;
                CurrentView = Herramienta6;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );

        GoToConfiguration = ReactiveCommand.Create(
            () =>
            {
                Herramienta1Activa = false;
                Herramienta2Activa = false;
                Herramienta3Activa = false;
                Herramienta4Activa = false;
                Herramienta5Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = true;
                AcercaDeActivo = false;
                CurrentView = Configuration;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        GoToAcercaDe = ReactiveCommand.Create(
            () =>
            {
                AcercaDeActivo = true;
                Herramienta1Activa = false;
                Herramienta2Activa = false;
                Herramienta3Activa = false;
                Herramienta4Activa = false;
                Herramienta5Activa = false;
                Herramienta6Activa = false;
                ConfiguracionActiva = false;
                CurrentView = AcercaDe;
                return Unit.Default;
            },
            outputScheduler: AvaloniaScheduler.Instance
        );
        
        _isDarkTheme = ConfigurationService.Current.UseDarkTheme ?? 
                       (Application.Current?.ActualThemeVariant == ThemeVariant.Dark);

        Application.Current?.GetObservable(Application.ActualThemeVariantProperty)
            .Subscribe(theme =>
            {
                IsDarkTheme = theme == ThemeVariant.Dark;
            });
        
        if (appConfig.UseCustomIcon)
            Avalonia.Threading.Dispatcher.UIThread.Post(LoadProfileImage, Avalonia.Threading.DispatcherPriority.Background);
        
        // Aplicar herramienta solicitada por línea de comandos
        ApplyStartupTool();
    }
    
    private void ApplyStartupTool()
    {
        var tool = StartupOptions.RequestedTool;
        if (string.IsNullOrEmpty(tool))
            return;

        switch (tool)
        {
            case "vcut":
            case "1":
                GoToHerramienta1.Execute().Subscribe();
                break;
            case "vcompress":
            case "2":
                GoToHerramienta2.Execute().Subscribe();
                break;
            case "vconvert":
            case "3":
                GoToHerramienta3.Execute().Subscribe();
                break;
            // etc para 4, 5, 6
            case "config":
                GoToConfiguration.Execute().Subscribe();
                break;
            case "about":
            case "acercade":
                GoToAcercaDe.Execute().Subscribe();
                break;
            default:
                Console.WriteLine($"[STARTUP] Herramienta desconocida: '{tool}'");
                break;
        }
    }
    
    public void LoadProfileImage()
    {
        var config = ConfigurationService.Current;
        
        if (config.UseCustomIcon)
        {
            var imagePath = config.ProfileImagePath ?? ProfileImageService.GetCurrentProfileImagePath();
            
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    var previous = UserImage;
                    UserImage = new Bitmap(imagePath);
                    previous?.Dispose();
                    ShowCustomIcon = true;
                }
                catch
                {
                    UserImage?.Dispose();
                    UserImage = null;
                    ShowCustomIcon = false;
                }
            }
            else
            {
                UserImage?.Dispose();
                UserImage = null;
                ShowCustomIcon = false;
            }
        }
        else
        {
            UserImage?.Dispose();
            UserImage = null;
            ShowCustomIcon = false;
        }
    }
    
    public void RefreshProfileImage()
    {
        LoadProfileImage();
    }
    
    private void ToggleTheme()
    {
        var app = (App)Application.Current!;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        bool isDark = app.RequestedThemeVariant == ThemeVariant.Dark;
        ConfigurationService.Current.UseDarkTheme = isDark;
        ConfigurationService.Save(ConfigurationService.Current);
        Configuration.UseDarkTheme = isDark;
    }
    
    private void OpenGitHub()
    {
        string url = "https://github.com/balta-dev";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Failed to open GitHub: {ex.Message}");
            ErrorService.Show(ex);
        }
    }
}