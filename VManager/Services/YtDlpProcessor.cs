using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using VManager.Services.Core;
using VManager.Services.Core.Media;
using VManager.Services.Models;

namespace VManager.Services;

public class YtDlpProcessor
{
    private string YtDlpPath => YtDlpManager.YtDlpPath;
    public LocalizationService L => LocalizationService.Instance;

    private static string? DetectBrowser()
    {
        if (OperatingSystem.IsWindows())
            return DetectBrowserWindows();

        if (OperatingSystem.IsLinux())
            return DetectBrowserLinux();

        if (OperatingSystem.IsMacOS())
            return DetectBrowserMac();

        return null;
    }

    private static string? DetectBrowserWindows()
    {
        try
        {
            const string path = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice";
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
            var progId = key?.GetValue("ProgId")?.ToString()?.ToLower();

            if (progId == null) return null;

            if (progId.Contains("chrome")) return "chrome";
            if (progId.Contains("edge")) return "edge";
            if (progId.Contains("firefox")) return "firefox";
            if (progId.Contains("brave")) return "brave";
            if (progId.Contains("opera")) return "opera";
            if (progId.Contains("vivaldi")) return "vivaldi";

            return null;
        }
        catch { return null; }
    }

    private static string? DetectBrowserLinux()
    {
        try
        {
            using var result = Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-settings",
                Arguments = "get default-web-browser",
                RedirectStandardOutput = true,
                UseShellExecute = false
            });

            if (result == null)
                return null;

            string output = result.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
            result.WaitForExit();

            if (output.Contains("chrome")) return "chrome";
            if (output.Contains("chromium")) return "chromium";
            if (output.Contains("firefox")) return "firefox";
            if (output.Contains("brave")) return "brave";
            if (output.Contains("opera")) return "opera";
            if (output.Contains("vivaldi")) return "vivaldi";

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectBrowserMac()
    {
        try
        {
            using var result = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"defaultbrowser\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            });

            if (result == null)
                return null;

            string output = result.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
            result.WaitForExit();

            if (output.Contains("chrome")) return "chrome";
            if (output.Contains("firefox")) return "firefox";
            if (output.Contains("safari")) return "safari";
            if (output.Contains("edge")) return "edge";
            if (output.Contains("opera")) return "opera";

            return null;
        }
        catch
        {
            return null;
        }
    }

    // ============================================================
    //                        PROGRESS
    // ============================================================

    private void ProcessYtDlpLine(string line, IProgress<YtDlpProgress>? progress)
    {
        Console.WriteLine("[YTDLP] " + line);
        
        if (line.Contains("Sleeping") && line.Contains("seconds"))
        {
            progress?.Report(new YtDlpProgress(
                0,
                "Esperando...",
                "Preparando..."
            ));
        }

        if (!line.StartsWith("[download]"))
            return;

        // REGEX
        var m = Regex.Match(line,
            @"(\d+(?:\.\d+)?)%.*?at\s+(\S+)\s+ETA\s+(\S+)",
            RegexOptions.IgnoreCase);

        if (m.Success)
        {
            double pct = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100;
            string speed = m.Groups[2].Value;
            string eta = m.Groups[3].Value;

            progress?.Report(new YtDlpProgress(pct, speed, eta));
            return;
        }

        var m2 = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
        if (m2.Success)
        {
            double pct = double.Parse(m2.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100;
            progress?.Report(new YtDlpProgress(pct, "", ""));
        }
    }
    
    private IEnumerable<string> BuildCookiesArguments()
    {
        var config = ConfigurationService.Current;

        // 1) Archivo de cookies
        if (config.UseCookiesFile && !string.IsNullOrWhiteSpace(config.CookiesFilePath))
        {
            if (File.Exists(config.CookiesFilePath))
            {
                yield return "--cookies";
                yield return config.CookiesFilePath;
                yield break;
            }
            else
            {
                Console.WriteLine("[YTDLP] Archivo de cookies configurado pero no existe.");
            }
        }

        // 2) Cookies del navegador
        string? browser = DetectBrowser();
        if (browser != null)
        {
            yield return "--cookies-from-browser";
            yield return browser;
            yield break;
        }
    }
    
    public async Task<(VideoInfo? Info, bool ShowHelp, bool UsedCookies)>
        GetVideoInfoWithDetectionAsync(string url, CancellationToken ct = default)
    {
        var (info, usedCookies) = await GetVideoInfoAsync(url, ct);

        if (info?.Formats == null || info.Formats.Count == 0)
            return (info, true, usedCookies);

        var usableHeights = info.Formats
            .Where(f =>
                f.Height.HasValue &&
                !string.IsNullOrEmpty(f.VideoCodec) &&
                f.VideoCodec != "none")
            .Select(f => f.Height!.Value)
            .Distinct()
            .ToList();

        if (usableHeights.Count == 0)
            return (info, true, usedCookies);

        int maxHeight = usableHeights.Max();
        bool showHelp = maxHeight <= 360 && usableHeights.Count <= 3;

        return (info, showHelp, usedCookies);
    }

    // ============================================================
    //                   OBTENER INFO DEL VIDEO
    // ============================================================

    public async Task<(VideoInfo? Info, bool UsedCookies)> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = await GetVideoInfoInternalAsync(url, useCookies: true, cancellationToken);
        if (result != null)
            return (result, true);
    
        Console.WriteLine("[YTDLP] Reintentando sin cookies...");
        var ex = new Exception("yt-dlp está teniendo problemas. Reintentando sin cookies...");
        ErrorService.Show(L["Errors.RetryingWithoutCookies"], null, L["Errors.CookiesWarningTitle"], "#FFFFA500");
        result = await GetVideoInfoInternalAsync(url, useCookies: false, cancellationToken);
        return (result, false);
    }

    private async Task<VideoInfo?> GetVideoInfoInternalAsync(string url, bool useCookies, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    
        if (useCookies)
            foreach (var arg in BuildCookiesArguments())
                psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add("--js-runtimes");
        psi.ArgumentList.Add($"deno:{DenoManager.DenoPath}");
        psi.ArgumentList.Add("--dump-json");
        psi.ArgumentList.Add(url);

        var process = new Process { StartInfo = psi };
    
        try
        {
            process.Start();
            Console.WriteLine($"[DEBUG] Comando: {psi.FileName} {string.Join(" ", psi.ArgumentList)}");

            string json = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            
            string? errorMessage = error.Split('\n')
                .FirstOrDefault(line => line.Contains("Cookies file must be Netscape formatted"))
                ?.Substring("ERROR:".Length)
                .Trim();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"Error obteniendo info: {error}");
                
                if (!string.IsNullOrEmpty(errorMessage))
                 ErrorService.Show(errorMessage);
                
                return null;
            }

            return JsonSerializer.Deserialize<VideoInfo>(json, VManagerJsonContext.Default.VideoInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener info del video: {ex.Message}");
            ErrorService.Show(ex);
            return null;
        }
    }

    // ============================================================
    //                   OBTENER INFO DE PLAYLIST
    // ============================================================

    /// <summary>
    /// Devuelve la info de una playlist usando --flat-playlist -J.
    /// El campo _type del JSON determina si es playlist o video suelto.
    /// </summary>
    public async Task<PlaylistInfo?> GetPlaylistInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = await GetPlaylistInfoInternalAsync(url, useCookies: true, cancellationToken);
        if (result != null)
            return result;

        Console.WriteLine("[YTDLP] Reintentando playlist sin cookies...");
        return await GetPlaylistInfoInternalAsync(url, useCookies: false, cancellationToken);
    }

    private async Task<PlaylistInfo?> GetPlaylistInfoInternalAsync(string url, bool useCookies, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (useCookies)
            foreach (var arg in BuildCookiesArguments())
                psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add("--flat-playlist");
        psi.ArgumentList.Add("-J"); // JSON completo de la playlist en un solo objeto
        psi.ArgumentList.Add("--js-runtimes");
        psi.ArgumentList.Add($"deno:{DenoManager.DenoPath}");
        psi.ArgumentList.Add(url);

        var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
            Console.WriteLine($"[DEBUG] Playlist cmd: {psi.FileName} {string.Join(" ", psi.ArgumentList)}");

            string json = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"[YTDLP] Error obteniendo playlist: {error}");
                return null;
            }

            return JsonSerializer.Deserialize<PlaylistInfo>(json, VManagerJsonContext.Default.PlaylistInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[YTDLP] Error al obtener info de playlist: {ex.Message}");
            ErrorService.Show(ex);
            return null;
        }
    }

    // ============================================================
    //                      DESCARGAR VIDEO
    // ============================================================
    
    public async Task<ProcessingResult> DownloadAsync(
        string url,
        string outputTemplate,
        IProgress<YtDlpProgress> progress,
        CancellationToken cancellationToken,
        string? formatId = null,
        bool useCookies = true,
        string? originalLanguage = null)
    {
        string safeOutput = OutputPathBuilder.SanitizeFilename(outputTemplate);
        
        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        if (useCookies)
            foreach (var arg in BuildCookiesArguments())
                psi.ArgumentList.Add(arg);

        psi.ArgumentList.Add("--newline");
        
        /*
         https://github.com/yt-dlp/yt-dlp/issues/15569
         web_safari deja de poder puede descargar m3u8 porque yt empezó a forzar SABR
         el problema es que yt-dlp todavía no entiende SABR
         https://github.com/yt-dlp/yt-dlp/issues/12482
        */
        psi.ArgumentList.Add("--js-runtimes");
        psi.ArgumentList.Add($"deno:{DenoManager.DenoPath}");
        psi.ArgumentList.Add("--merge-output-format");
        psi.ArgumentList.Add("mp4");
        
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(safeOutput);

        if (!string.IsNullOrEmpty(formatId))
        {
            if (formatId == "0")
            {
                string audioSelector = !string.IsNullOrEmpty(originalLanguage)
                    ? $"bestaudio[language={originalLanguage}]/bestaudio"
                    : "bestaudio";

                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(audioSelector);
                psi.ArgumentList.Add("-x");
                psi.ArgumentList.Add("--audio-format");
                psi.ArgumentList.Add("mp3");
            }
            else if (formatId == "1")
            {
                string audioSelector = !string.IsNullOrEmpty(originalLanguage)
                    ? $"bestaudio[language={originalLanguage}]/bestaudio"
                    : "bestaudio";

                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(audioSelector);
                psi.ArgumentList.Add("-x");
                psi.ArgumentList.Add("--audio-format");
                psi.ArgumentList.Add("wav");
            }
            else
            {
                string baseFormatId = formatId.Contains('-') ? formatId.Split('-')[0] : formatId;
    
                string formatSelector;
                if (!string.IsNullOrEmpty(originalLanguage))
                {
                    // Intentar el formato base con el idioma original, luego fallbacks
                    formatSelector = 
                        $"{baseFormatId}[language={originalLanguage}]+bestaudio[language={originalLanguage}]" +
                        $"/{baseFormatId}[language={originalLanguage}]" +
                        $"/{baseFormatId}+bestaudio[language={originalLanguage}]" +
                        $"/{baseFormatId}+bestaudio/best";
                }
                else
                {
                    formatSelector = $"{baseFormatId}+bestaudio/best";
                }
    
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(formatSelector);
            }
        }

        psi.ArgumentList.Add(url);

        var process = new Process { StartInfo = psi };

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Registrar cancelación para matar el proceso
            linkedCts.Token.Register(() =>
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        Console.WriteLine("[DEBUG] Proceso yt-dlp cancelado.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[DEBUG] Error matando proceso: " + ex.Message);
                        ErrorService.Show(ex);
                    }

                    // Borrar archivo parcial
                    if (File.Exists(safeOutput))
                    {
                        try
                        {
                            File.Delete(safeOutput);
                            Console.WriteLine("[DEBUG] Archivo parcial eliminado tras cancelación.");
                        }
                        catch (IOException ex)
                        {
                            Console.WriteLine("[DEBUG] No se pudo eliminar archivo parcial: " + ex.Message);
                        }
                    }
                }
            });

            Console.WriteLine("[YTDLP CMD] " + psi.FileName + " " + string.Join(" ", psi.ArgumentList));
            process.Start();

            // Leer stdout y stderr con respeto al token
            Task readStdout = Task.Run(async () =>
            {
                using var r = process.StandardOutput;
                string? line;
                while ((line = await r.ReadLineAsync()) != null)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    ProcessYtDlpLine(line, progress);
                }
            }, linkedCts.Token);

            Task readStderr = Task.Run(async () =>
            {
                using var r = process.StandardError;
                string? line;
                while ((line = await r.ReadLineAsync()) != null)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    ProcessYtDlpLine(line, progress);
                }
            }, linkedCts.Token);

            await Task.WhenAll(readStdout, readStderr);
            await process.WaitForExitAsync(linkedCts.Token);

            if (linkedCts.Token.IsCancellationRequested)
            {
                return new ProcessingResult(false, "Operación cancelada por el usuario.");
            }

            if (process.ExitCode == 0)
                return new ProcessingResult(true, "Descarga completada");

            return new ProcessingResult(false, "Error ejecutando yt-dlp");
        }
        catch (OperationCanceledException)
        {
            return new ProcessingResult(false, "Operación cancelada por el usuario.");
        }
        catch (Exception ex)
        {
            ErrorService.Show(ex);
            return new ProcessingResult(false, $"Error: {ex.Message}");
        }
    }
}
