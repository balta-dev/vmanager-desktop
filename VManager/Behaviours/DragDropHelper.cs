using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ReactiveUI;
using Avalonia.Platform.Storage;
using DynamicData;

namespace VManager.Behaviours
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public static class DragDropHelper
    {
        // Enable drag & drop
        public static readonly AttachedProperty<bool> EnableFileDropProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "EnableFileDrop", typeof(DragDropHelper));

        public static bool GetEnableFileDrop(Control control) => control.GetValue(EnableFileDropProperty);
        public static void SetEnableFileDrop(Control control, bool value) => control.SetValue(EnableFileDropProperty, value);

        // ViewModel property where files are written
        public static readonly AttachedProperty<string> DropTargetProperty =
            AvaloniaProperty.RegisterAttached<Control, string>(
                "DropTarget", typeof(DragDropHelper));
        
        private static readonly AttachedProperty<bool> IsDragOverProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "IsDragOver", typeof(DragDropHelper));
        
        public static readonly AttachedProperty<bool> AllowLinksProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "AllowLinks", typeof(DragDropHelper));

        public static string GetDropTarget(Control control) => control.GetValue(DropTargetProperty);
        public static void SetDropTarget(Control control, string value) => control.SetValue(DropTargetProperty, value);
        
        public static readonly AttachedProperty<bool> AllowTxtProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "AllowTxt", typeof(DragDropHelper));
        public static bool GetAllowTxt(Control control) => control.GetValue(AllowTxtProperty);
        public static void SetAllowTxt(Control control, bool value) => control.SetValue(AllowTxtProperty, value);
        public static bool GetAllowLinks(Control control) => control.GetValue(AllowLinksProperty);
        public static void SetAllowLinks(Control control, bool value) => control.SetValue(AllowLinksProperty, value);

        // Only video and audio
        private static readonly string[] VideoExtensions = 
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".3gp"
        };
        
        private static readonly string[] AudioExtensions = 
        {
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma"
        };
        
        private static readonly string[] TextExtensions =
        {
            ".txt"
        };
        
        // Simple property to also enable audio
        public static readonly AttachedProperty<bool> AllowAudioProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "AllowAudio", typeof(DragDropHelper));

        public static bool GetAllowAudio(Control control) => control.GetValue(AllowAudioProperty);
        public static void SetAllowAudio(Control control, bool value) => control.SetValue(AllowAudioProperty, value);

        static DragDropHelper()
        {
            EnableFileDropProperty.Changed.AddClassHandler<Control>((control, e) =>
            {
                if (e.NewValue is bool enabled && enabled)
                {
                    DragDrop.SetAllowDrop(control, true);
                    control.AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
                    control.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, handledEventsToo: true);
                    control.AddHandler(DragDrop.DropEvent, OnDropFile, handledEventsToo: true);
                }
                else
                {
                    DragDrop.SetAllowDrop(control, false);
                    control.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
                    control.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                    control.RemoveHandler(DragDrop.DropEvent, OnDropFile);
                }
            });
        }

        // Save original background
        private static readonly AttachedProperty<IBrush?> OriginalBackgroundProperty =
            AvaloniaProperty.RegisterAttached<Control, IBrush?>("OriginalBackground", typeof(DragDropHelper));

        private static void OnDragOver(object? sender, DragEventArgs e)
        {
            
            if (!e.DataTransfer.Contains(DataFormat.File))
            {
                if (sender is Control ctrl && GetAllowLinks(ctrl) && e.DataTransfer.Contains(DataFormat.Text))
                {
                    var text = e.DataTransfer.TryGetText();
                    bool isUrl = Uri.TryCreate(text, UriKind.Absolute, out var uri)
                                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

                    e.DragEffects = isUrl ? DragDropEffects.Copy : DragDropEffects.None;
                    e.Handled = isUrl;

                    if (sender is Border linkBorder)
                    {
                        if (linkBorder.GetValue(OriginalBackgroundProperty) == null)
                            linkBorder.SetValue(OriginalBackgroundProperty, linkBorder.Background);
                        linkBorder.Background = new SolidColorBrush(isUrl ? Colors.LightGreen : Colors.LightCoral, 0.3);
                    }
                    return;
                }

                e.DragEffects = DragDropEffects.None;
                return;
            }

            var files = e.DataTransfer.TryGetFiles();
            if (files == null || !files.Any())
            {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            if (sender is Control control)
            {
                bool allowAudio = GetAllowAudio(control);
                bool allowTxt = GetAllowTxt(control);
                control.SetValue(IsDragOverProperty, true);

                bool hasValidFile;
                
                if (allowTxt && !allowAudio) // solo TXT
                {
                    hasValidFile = files.Any(file =>
                    {
                        var extension = System.IO.Path.GetExtension(file.Path.LocalPath).ToLowerInvariant();
                        return TextExtensions.Contains(extension);
                    });
                }
                else // videos y audios
                {
                    hasValidFile = files.Any(file =>
                    {
                        var extension = System.IO.Path.GetExtension(file.Path.LocalPath).ToLowerInvariant();
                        return VideoExtensions.Contains(extension) ||
                               (allowAudio && AudioExtensions.Contains(extension));
                    });
                }

                if (!hasValidFile)
                {
                    e.DragEffects = DragDropEffects.None;
                    if (sender is Border border)
                    {
                        if (border.GetValue(OriginalBackgroundProperty) == null)
                        {
                            border.SetValue(OriginalBackgroundProperty, border.Background);
                        }
                        border.Background = new SolidColorBrush(Colors.LightCoral, 0.3);
                    }
                    return;
                }
            }

            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;

            if (sender is Border validBorder)
            {
                if (validBorder.GetValue(OriginalBackgroundProperty) == null)
                {
                    validBorder.SetValue(OriginalBackgroundProperty, validBorder.Background);
                }

                validBorder.Background = new SolidColorBrush(Colors.LightGreen, 0.3);
            }
        }


        private static async void OnDragLeave(object? sender, DragEventArgs e)
        {
            if (sender is not Border border)
                return;

            border.SetValue(IsDragOverProperty, false);

            await System.Threading.Tasks.Task.Yield();
            
            var mousePos = e.GetPosition(border);
            
            if (border.GetValue(IsDragOverProperty))
                return;

            // Salvaguarda mantenida
            if (mousePos.X > 0 && mousePos.X < border.Bounds.Width &&
                mousePos.Y > 0 && mousePos.Y < border.Bounds.Height)
                return;
            
            var original = border.GetValue(OriginalBackgroundProperty);
            border.Background = original ?? Brushes.Transparent;
            border.ClearValue(OriginalBackgroundProperty);
        }

        private static async void OnDropFile(object? sender, DragEventArgs e)
        {
            Console.WriteLine("=== OnDropFile iniciado ===");
            
            if (sender is not Control control)
            {
                Console.WriteLine("Sender no es Control");
                return;
            }
            
            if (!e.DataTransfer.Contains(DataFormat.File) && GetAllowLinks(control))
            {
                var text = e.DataTransfer.TryGetText();

                if (sender is Border linkBorderCleanup)
                {
                    var originalBg = linkBorderCleanup.GetValue(OriginalBackgroundProperty);
                    linkBorderCleanup.Background = originalBg ?? Brushes.Transparent;
                    linkBorderCleanup.ClearValue(OriginalBackgroundProperty);
                }

                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    var dcLink = FindDataContext(control);
                    var addUrlMethod = dcLink?.GetType().GetMethod("AddUrl", new[] { typeof(string) });

                    if (dcLink != null && addUrlMethod != null)
                    {
                        addUrlMethod.Invoke(dcLink, new object[] { text! });
                        ShowSuccessFeedback(control);
                    }
                    else
                    {
                        ShowErrorFeedback(control);
                    }
                }
                else
                {
                    ShowErrorFeedback(control);
                }

                e.Handled = true;
                return;
            }
            
            // IMPORTANTE: Limpiar el color del drag antes de procesar
            if (sender is Border borderCleanup)
            {
                var originalBg = borderCleanup.GetValue(OriginalBackgroundProperty);
                if (originalBg != null)
                {
                    Console.WriteLine($"Limpiando background original: {originalBg}");
                    borderCleanup.Background = originalBg;
                    borderCleanup.ClearValue(OriginalBackgroundProperty);
                }
                else
                {
                    Console.WriteLine("No hay OriginalBackgroundProperty guardado");
                    // Si no hay original guardado, forzar a transparent
                    borderCleanup.Background = Brushes.Transparent;
                }
            }

            var files = e.DataTransfer?.TryGetFiles() ?? Array.Empty<IStorageFile>();
            if (!files.Any())
            {
                Console.WriteLine("No hay archivos");
                return;
            }

            // AGREGAR DISTINCT acá para eliminar duplicados del DataTransfer
            var uniqueFiles = files.DistinctBy(f => f.Path.LocalPath).ToList();

            Console.WriteLine($"Archivos detectados: {files.Count()}");
            Console.WriteLine($"Archivos únicos: {uniqueFiles.Count()}");

            bool allowAudio = GetAllowAudio(control);
            bool allowTxt = GetAllowTxt(control);

            Console.WriteLine($"AllowAudio: {allowAudio}, AllowTxt: {allowTxt}");

            var dc = FindDataContext(control);
            if (dc == null)
            {
                Console.WriteLine("DataContext no encontrado");
                ShowErrorFeedback(control);
                return;
            }

            Console.WriteLine($"DataContext encontrado: {dc.GetType().Name}");

            // Determinar el tipo de archivo que esperamos según las propiedades
            if (allowTxt && !allowAudio) 
            {
                Console.WriteLine("Modo TXT");
                // Modo TXT: solo aceptar archivos .txt
                var txtPaths = files
                    .Where(f => TextExtensions.Contains(System.IO.Path.GetExtension(f.Path.LocalPath).ToLowerInvariant()))
                    .Select(f => f.Path.LocalPath)
                    .ToList();

                Console.WriteLine($"Archivos TXT encontrados: {txtPaths.Count}");

                if (!txtPaths.Any())
                {
                    Console.WriteLine("No hay archivos TXT válidos - mostrando error");
                    ShowErrorFeedback(control);
                    e.Handled = true; 
                    Console.WriteLine("=== OnDropFile finalizado (TXT inválido) ===");
                    return;
                }

                var cookiesProp = dc.GetType().GetProperty("CookiesFilePath");
                Console.WriteLine($"CookiesFilePath existe: {cookiesProp != null}, CanWrite: {cookiesProp?.CanWrite}");
                
                if (cookiesProp != null && cookiesProp.CanWrite)
                {
                    cookiesProp.SetValue(dc, txtPaths.First());
                    Console.WriteLine($"CookiesFilePath asignado: {txtPaths.First()}");
                    if (dc is ReactiveObject reactiveObj)
                        reactiveObj.RaisePropertyChanged("CookiesFilePath");
                    
                    ShowSuccessFeedback(control);
                    e.Handled = true;
                    Console.WriteLine("=== OnDropFile finalizado (TXT exitoso) ===");
                }
                else
                {
                    Console.WriteLine("No se pudo asignar CookiesFilePath - mostrando error");
                    ShowErrorFeedback(control);
                    e.Handled = true;
                    Console.WriteLine("=== OnDropFile finalizado (TXT error asignación) ===");
                }
                return;
            }
            else 
            {
                Console.WriteLine("Modo Video/Audio");
                // Modo Video/Audio
                var validPaths = uniqueFiles
                    .Where(f =>
                    {
                        var ext = System.IO.Path.GetExtension(f.Path.LocalPath).ToLowerInvariant();
                        return VideoExtensions.Contains(ext) || (allowAudio && AudioExtensions.Contains(ext));
                    })
                    .Select(f => f.Path.LocalPath)
                    .ToList();

                if (!validPaths.Any())
                {
                    Console.WriteLine("No hay archivos video/audio válidos");
                    ShowErrorFeedback(control);
                    return;
                }

                Console.WriteLine($"Paths válidos: {validPaths.Count}");
                foreach (var path in validPaths)
                {
                    Console.WriteLine($"  - {path}");
                }

                // Intentamos obtener la propiedad VideoPaths (ObservableCollection<string>)
                var listProp = dc.GetType().GetProperty("VideoPaths");
                var singleProp = dc.GetType().GetProperty("VideoPath");
                var loadDurationMethod = dc.GetType().GetMethod("LoadVideoDurationAsync");

                Console.WriteLine($"VideoPaths existe: {listProp != null}, VideoPath existe: {singleProp != null}");

                if (listProp != null && listProp.CanWrite)
                {
                    if (listProp.GetValue(dc) is ObservableCollection<string> currentList)
                    {
                        // Filtrar solo los que no están ya en la lista
                        var newPaths = validPaths.Where(path => !currentList.Contains(path)).ToList();
    
                        if (newPaths.Any())
                        {
                            currentList.AddRange(newPaths);
                            Console.WriteLine($"Agregados {newPaths.Count} items nuevos a lista existente");
                        }
                        else
                        {
                            Console.WriteLine("Todos los archivos ya están en la lista");
                            ShowErrorFeedback(control); // O un feedback diferente para duplicados
                        }
                    }
                    else
                    {
                        listProp.SetValue(dc, new ObservableCollection<string>(validPaths));
                        Console.WriteLine("Lista nueva creada y asignada");
                    }

                    // Forzar notificación ReactiveUI
                    if (dc is ReactiveObject reactiveObj)
                    {
                        reactiveObj.RaisePropertyChanged("VideoPaths");
                        Console.WriteLine("RaisePropertyChanged('VideoPaths') ejecutado");
                    }
                }

                if (singleProp != null && singleProp.CanWrite && validPaths.Any())
                {
                    singleProp.SetValue(dc, validPaths.First());
                    Console.WriteLine($"VideoPath asignado: {validPaths.First()}");
                    if (dc is ReactiveObject reactiveObj)
                    {
                        reactiveObj.RaisePropertyChanged("VideoPath");
                        Console.WriteLine("RaisePropertyChanged('VideoPath') ejecutado");
                        if (loadDurationMethod != null)
                        {
                            Console.WriteLine("Llamando a LoadVideoDurationAsync vía reflexión...");
                            await (Task)loadDurationMethod.Invoke(dc, new object[] { validPaths.First() })!;
                        }
                    }
                }

                Console.WriteLine("Mostrando success feedback");
                ShowSuccessFeedback(control);
            }

            e.Handled = true;
            Console.WriteLine("=== OnDropFile finalizado ===");
        }
        
        private static async void ShowSuccessFeedback(Control control)
        {
            Console.WriteLine("ShowSuccessFeedback iniciado");
            if (control is Border border)
            {
                // Green flash
                border.Background = new SolidColorBrush(Colors.LightGreen, 0.6);
                await System.Threading.Tasks.Task.Delay(200);
                
                // Volver a transparente siempre
                border.Background = Brushes.Transparent;
                
                Console.WriteLine("ShowSuccessFeedback completado");
            }
        }

        private static async void ShowErrorFeedback(Control control)
        {
            Console.WriteLine("ShowErrorFeedback iniciado");
            if (control is Border border)
            {
                // Red flash
                border.Background = new SolidColorBrush(Colors.LightCoral, 0.6);
                await System.Threading.Tasks.Task.Delay(300);
                
                // Volver a transparente siempre
                border.Background = Brushes.Transparent;
                
                Console.WriteLine("ShowErrorFeedback completado");
            }
        }

        private static object? FindDataContext(Control control)
        {
            var current = control;
            while (current != null)
            {
                if (current.DataContext != null)
                    return current.DataContext;
                current = current.Parent as Control;
            }
            return null;
        }
    }
}