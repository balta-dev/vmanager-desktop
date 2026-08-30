using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Converters;
using VManager.Behaviours;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using VManager.Controls;
using VManager.Services;
using VManager.Models;
using VManager.Services.Core;
using VManager.Services.Models;
using VManager.ViewModels;

namespace VManager.Views
{
    public partial class MainWindow : Window
    {
        public static Stopwatch? StartupStopwatch;
        private AppConfig Config => ConfigurationService.Current;

        private void AdaptSplitView()
        {
            var splitViewGrid = this.FindControl<Grid>("SplitViewGrid");
            
            if (splitViewGrid != null)
            {
                splitViewGrid.Margin = Config.ShowThemeToggleButton
                    ? new Thickness (10, 50, 10, 10)
                    : new Thickness (10, 7, 10, 10);
            }
        }
        
        private void ApplyDecorationMode()
        {
            
            if (Config.UseCustomDecorations)
            {
                WindowDecorations = WindowDecorations.None;
                TransparencyLevelHint = new[]
                {
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.Transparent
                };
            }
            else
            {
                WindowDecorations = WindowDecorations.Full;
                TransparencyLevelHint = new[]
                {
                    WindowTransparencyLevel.None
                };
            }

            // Controles del AXAML
            var resizeGrip = this.FindControl<ResizeGrip>("TheResizeGrip");
            var titleBar = this.FindControl<CustomTitleBar>("TheTitleBar");
            var rootBorder = this.FindControl<Border>("RootBorder");
            var mainGrid = this.FindControl<Grid>("MainGrid");

            if (resizeGrip != null)
            {
                resizeGrip.IsVisible = Config.UseCustomDecorations;
                resizeGrip.IsEnabled = Config.UseCustomDecorations;
            }

            if (titleBar != null)
            {
                titleBar.IsVisible = Config.UseCustomDecorations;
                titleBar.IsEnabled = Config.UseCustomDecorations;
            }

            if (mainGrid != null)
            {
                mainGrid.RowDefinitions = Config.UseCustomDecorations
                    ? new RowDefinitions("32,*")
                    : new RowDefinitions("0,*");
            }
            
            if (rootBorder != null)
            {
                rootBorder.CornerRadius = Config.UseCustomDecorations 
                    ? new CornerRadius(10) 
                    : new CornerRadius(0);
                rootBorder.BorderThickness = Config.UseCustomDecorations 
                    ? new Thickness(1) 
                    : new Thickness(0);
            }
            
        }
        
        public MainWindow()
        {
            if (Config.UseCustomDecorations)
            {
                WindowDecorations = WindowDecorations.None;
                TransparencyLevelHint = new[]
                {
                    WindowTransparencyLevel.AcrylicBlur,
                    //WindowTransparencyLevel.Transparent
                };
            }
            
            InitializeComponent();
            
            var accentObs = this.GetResourceObservable("SystemAccentColor")!
                .OfType<Color>()
                .DistinctUntilChanged()
                .Where(c => c != Color.FromArgb(0xFF, 0x00, 0x78, 0xD7)); //default de systemaccent antes de tomarlo del resource padre

            var themeObs = this.GetObservable(ActualThemeVariantProperty)
                .Select(_ => default(Color?)); // no aporta color, solo trigger

            Observable.Merge(accentObs.Select(c => (Color?)c), themeObs)
                .Subscribe(_ =>
                {
                    ApplyCustomAccent();
                });
            
            this.Opened += (_, _) => {
                StartupStopwatch?.Stop();
                Console.WriteLine($"[DEBUG]: Startup en {StartupStopwatch!.ElapsedMilliseconds} ms");

                Dispatcher.UIThread.Post(() =>
                {
                    SoundBehaviour.Attach(this);

                    if (DataContext is MainWindowViewModel vm)
                    {
                        var configVM = vm.Configuration;
                        configVM.WhenAnyValue(x => x.SelectedColor)
                            .Subscribe(_ => ApplyCustomAccent());
                        configVM.WhenAnyValue(x => x.UseCustomDecorations)
                            .Subscribe(_ => ApplyDecorationMode());
                        configVM.WhenAnyValue(x => x.ShowThemeToggleButton)
                            .Subscribe(_ => AdaptSplitView());
                    }
                }, DispatcherPriority.Background);
            };
            
            LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
            
            this.Opened += async (_, _) =>
            {
                await Task.Delay(3000);
                LaunchUpdater();
            };
            this.Closing += MainWindow_Closing;
            this.Closed += (_, _) =>
                LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
            this.KeyDown += OnKeyDown;
            MainSplitView.AddHandler(
                InputElement.KeyDownEvent,
                new EventHandler<KeyEventArgs>(MainSplitview_OnPreviewKeyDown),
                RoutingStrategies.Tunnel);
        }
        
        private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LocalizationService.CurrentLanguage) || e.PropertyName == "Item[]")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (DataContext is MainWindowViewModel vm)
                    {
                        var backup = DataContext;
                        DataContext = null;
                        DataContext = backup;
                    }
                });
            }
        }
        
        private void MainSplitview_OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true; // ❌ Enter queda bloqueado en toda la sidebar
            }
        }
        
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

            // Si estás en un TextBox → NO disparamos hotkeys
            if (focused is TextBox)
                return;
            
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (this.WindowState == WindowState.FullScreen)
                    this.WindowState = WindowState.Normal;
                else
                    this.WindowState = WindowState.FullScreen;
            }
            
            if (DataContext is not MainWindowViewModel vm)
                return;

            switch (e.Key)
            {
                case Key.D1:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToHerramienta1.Execute().Subscribe();
                    break;
                case Key.D2:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToHerramienta2.Execute().Subscribe();
                    break;
                case Key.D3:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToHerramienta3.Execute().Subscribe();
                    break;
                case Key.D4:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToHerramienta4.Execute().Subscribe();
                    break;
                case Key.D5:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToHerramienta5.Execute().Subscribe();
                    break;
                case Key.D6:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToHerramienta6.Execute().Subscribe();
                    break;
                case Key.D7:
                    _ = SoundManager.Play("click.wav");
                    vm.GoToAcercaDe.Execute().Subscribe();
                    break;
            }
        }
        
        private static bool VersionsAreEqual(Version? a, Version? b)
        {
            if (a == null || b == null)
                return false;

            // Normalizamos build y revision si vienen como -1
            int buildA = a.Build < 0 ? 0 : a.Build;
            int buildB = b.Build < 0 ? 0 : b.Build;
            int revA = a.Revision < 0 ? 0 : a.Revision;
            int revB = b.Revision < 0 ? 0 : b.Revision;

            return a.Major == b.Major &&
                   a.Minor == b.Minor &&
                   buildA == buildB &&
                   revA == revB;
        }
        
        private async void LaunchUpdater()
        {
            string updaterFile = Path.Combine(AppContext.BaseDirectory,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Updater.exe" : "Updater");

            string cacheFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VManager", "cache", "update_cache.json");

            try
            {
                UpdateInfo? cached = null;

                // Solo intentar leer si el archivo existe
                if (File.Exists(cacheFilePath))
                {
                    var json = await File.ReadAllTextAsync(cacheFilePath);
                    cached = JsonSerializer.Deserialize(
                        json.AsSpan(),
                        VManagerJsonContext.Default.UpdateInfo
                    );
                }
                
                Console.WriteLine("Buscando actualizaciones...");
                Console.WriteLine($"Version cacheada: {cached?.CurrentVersion}");
                Console.WriteLine($"Version de Assembly: {Assembly.GetEntryAssembly()?.GetName().Version}");
                
                // Si no hay cache o las versiones no coinciden, lanzar updater
                if (cached == null || !VersionsAreEqual(cached.CurrentVersion, Assembly.GetEntryAssembly()?.GetName().Version!))
                {
                    Console.WriteLine("Empezando Updater...");
                    if (File.Exists(updaterFile))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = updaterFile,
                            UseShellExecute = true,
                        });
                        
                        File.Delete(cacheFilePath);
                    }
                    else
                    {
                        Console.WriteLine("Updater no encontrado.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al lanzar updater: {ex}");
                var owner = TopLevel.GetTopLevel(this) as Window;
            }
        }
        
        private bool _allowClose = false;

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowClose)
                return; // Permitir el cierre

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow?.DataContext is MainWindowViewModel mainVM)
                {
                    // Verificar si hay alguna herramienta corriendo
                    var runningTools = mainVM.Tools.Where(t => t.IsOperationRunning).ToList();

                    if (runningTools.Any())
                    {
                        e.Cancel = true; // Cancelamos el cierre inicialmente

                        // Mostrar diálogo de cancelación usando la herramienta activa (CurrentView)
                        if (mainVM.CurrentView is ViewModelBase current)
                        {
                            bool shouldClose = await current.ShowCancelDialogInMainWindow(fromWindowClose: true);

                            if (shouldClose)
                            {
                                // Cancelar TODAS las operaciones en ejecución
                                foreach (var tool in runningTools)
                                {
                                    tool.RequestCancelOperation();
                                }

                                _allowClose = true;
                                this.Close(); // Cerrar la ventana
                            }
                            // Si el usuario cancela el diálogo, la ventana queda abierta
                        }
                    }
                    // Si ninguna herramienta está corriendo, se permite cerrar sin diálogo
                }
            }
        }
        
        public void ApplyCustomAccent(ThemeVariant? theme = null)
        {
            var actualTheme = theme ?? ActualThemeVariant;
            var accent = GetSystemAccentColor();
            
            var adjustedAccent = AdjustColorForAccentTheme(accent, actualTheme);
            var foreground = GetContrastingColor(adjustedAccent);
            Application.Current!.Resources["AccentBrush"] = new SolidColorBrush(adjustedAccent);
            Application.Current.Resources["AccentForegroundBrush"] = new SolidColorBrush(foreground);

            // Derivados para hover/pressed
            Application.Current.Resources["AccentBrushLight"] = new SolidColorBrush(LightenColor(adjustedAccent, 0.3)); // hover
            Application.Current.Resources["AccentBrushDark"] = new SolidColorBrush(DarkenColor(adjustedAccent, 0.15));   // pressed
            
            // Sincronizar slider — acá los colores ya están resueltos
            Application.Current.Resources["SliderThumbBackground"] = new SolidColorBrush(adjustedAccent);
            Application.Current.Resources["SliderThumbBackgroundPointerOver"] =
                Application.Current.Resources["AccentBrushLight"];
            Application.Current.Resources["SliderThumbBackgroundPressed"] =
                Application.Current.Resources["AccentBrushDark"];
            Application.Current.Resources["SliderTrackValueFill"] = new SolidColorBrush(adjustedAccent);
            Application.Current.Resources["SliderTrackValueFillPointerOver"] = 
                Application.Current.Resources["AccentBrushLight"];
            Application.Current.Resources["SliderTrackValueFillPressed"] =
                Application.Current.Resources["AccentBrushDark"];
            
            var redButton = Color.FromArgb(0xFF, 0xBF, 0x24, 0x24);
            var adjustedRed = AdjustColorForAccentTheme(redButton, actualTheme);
            Application.Current.Resources["RedButtonBrush"] = new SolidColorBrush(adjustedRed);
            var redForeground = GetContrastingColor(adjustedRed);
            Application.Current.Resources["RedButtonForegroundBrush"] = new SolidColorBrush(redForeground);
            
        }
        
        private Color GetSystemAccentColor()
        {
            var customColor = ConfigurationService.Current.SelectedColor;
            if (customColor.HasValue)
            {
                Console.WriteLine($"[GetSystemAccentColor] Usando color custom: {customColor.Value}");
                return customColor.Value;
            }

            // Intentar usar el recurso SystemAccentColor
            if (Application.Current?.TryGetResource("SystemAccentColor", null, out var value) == true && value is Color accent)
            {
                Console.WriteLine($"[GetSystemAccentColor] Usando color de sistema: {accent}");
                return accent;
            }
            
            
            return Color.FromArgb(0xFF, 0xFF, 0x00, 0x00); // acá es IMPOSIBLE que llegue
         
        }
        
        private double GetRelativeLuminance(Color color)
        {
            double R = color.R / 255.0;
            double G = color.G / 255.0;
            double B = color.B / 255.0;

            R = (R <= 0.03928) ? R / 12.92 : Math.Pow((R + 0.055) / 1.055, 2.4);
            G = (G <= 0.03928) ? G / 12.92 : Math.Pow((G + 0.055) / 1.055, 2.4);
            B = (B <= 0.03928) ? B / 12.92 : Math.Pow((B + 0.055) / 1.055, 2.4);

            return 0.2126 * R + 0.7152 * G + 0.0722 * B;
        }

        private double GetContrastRatio(Color c1, Color c2)
        {
            var l1 = GetRelativeLuminance(c1);
            var l2 = GetRelativeLuminance(c2);
            if (l1 < l2) (l1, l2) = (l2, l1);
            return (l1 + 0.05) / (l2 + 0.05);
        }
        private Color LightenColor(Color color, double factor)
        {
            byte Adjust(byte c) => (byte)Math.Clamp(c + (255 - c) * factor, 0, 255);
            return new Color(color.A, Adjust(color.R), Adjust(color.G), Adjust(color.B));
        }
        
        private Color GetContrastingColor(Color background, double minContrast = 4.5)
        {
            var white = Colors.White;
            var black = Colors.Black;

            var contrastWithWhite = GetContrastRatio(background, white);
            var contrastWithBlack = GetContrastRatio(background, black);

            // Si el fondo es muy oscuro, usar blanco; si es muy claro, usar negro
            return (GetRelativeLuminance(background) < 0.5) ? white : black;
        }
        private Color AdjustColorForAccentTheme(Color baseColor, ThemeVariant theme, double minContrast = 4.5)
        {
            Color adjusted = baseColor;

            // Priorizar ajuste según tema
            bool lighten = theme == ThemeVariant.Dark; // Aclarar en modo oscuro, oscurecer en modo claro
            double factor = 0.1; // Paso inicial del 10%
            double luminanceThreshold = 0.2; // Umbral para considerar el color "muy oscuro"

            for (int i = 0; i < 30; i++)
            {
                byte Adjust(byte c) => (byte)Math.Clamp(lighten ? c + (255 - c) * factor : c * (1 - factor), 0, 255);

                adjusted = new Color(adjusted.A,
                    Adjust(adjusted.R),
                    Adjust(adjusted.G),
                    Adjust(adjusted.B));

                // Verificar contraste con el fondo
                double contrast = GetContrastRatio(adjusted, (theme == ThemeVariant.Dark) ? Colors.Black : Colors.White);
                if (contrast >= minContrast)
                    break;

                // Reducir factor si no se alcanza el contraste
                if (i > 10 && contrast < minContrast * 0.9)
                    factor *= 0.8;
            }

            // Si el color ajustado es muy oscuro (luminancia < umbral) y estamos en modo claro, forzar un ajuste adicional
            double luminance = GetRelativeLuminance(adjusted);
            if ((theme == ThemeVariant.Light && luminance < luminanceThreshold) ||
                (theme == ThemeVariant.Dark && luminance > 1 - luminanceThreshold))
            {
                adjusted = theme == ThemeVariant.Dark ? DarkenColor(adjusted, 0.3) : LightenColor(adjusted, 0.3);
            }

            return adjusted;
        }
        
        private Color DarkenColor(Color color, double factor)
        {
            byte Adjust(byte c) => (byte)Math.Clamp(c * (1 - factor), 0, 255);
            return new Color(color.A, Adjust(color.R), Adjust(color.G), Adjust(color.B));
        }
        
    }
}