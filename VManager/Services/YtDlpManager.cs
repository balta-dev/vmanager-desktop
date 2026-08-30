using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace VManager.Services;

public static class YtDlpManager
{
    public static string YtDlpPath { get; private set; } = string.Empty;

    // Carpeta persistente, hermana de "Themes"
    private static string AppRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    private static string BinariesPath => Path.Combine(AppRoot, "Binaries");

    private static readonly SemaphoreSlim _extractLock = new(1, 1);

    private static Task? _initializeTask;
    private static readonly object InitLock = new();

    /// <summary>Inicializa yt-dlp bajo demanda (p. ej. al abrir VDownload).</summary>
    public static Task EnsureInitialized()
    {
        if (_initializeTask != null)
            return _initializeTask;

        lock (InitLock)
            return _initializeTask ??= Initialize();
    }

    public static async Task Initialize()
    {
        string targetFile = OperatingSystem.IsWindows() ? "yt-dlp.exe"
                          : OperatingSystem.IsMacOS() ? "yt-dlp_macos"
                          : "yt-dlp";

        Directory.CreateDirectory(BinariesPath);
        YtDlpPath = Path.Combine(BinariesPath, targetFile);

        if (File.Exists(YtDlpPath) && !OperatingSystem.IsWindows())
            Process.Start("chmod", $"+x \"{YtDlpPath}\"")?.WaitForExit();

        bool needsExtract = !File.Exists(YtDlpPath) || !await TestYtDlpAsync();

        if (needsExtract)
        {
            Console.WriteLine("[YTDLP] Extrayendo versión embebida…");
            await ExtractForOS(targetFile);
        }

        Console.WriteLine($"[YTDLP] path: {YtDlpPath}");

        _ = TryAutoUpdateAsync();
    }

    private static async Task ExtractForOS(string targetFile)
    {
        await _extractLock.WaitAsync();
        try
        {
            // Doble chequeo por si otro hilo ya extrajo mientras esperábamos
            if (File.Exists(YtDlpPath) && await TestYtDlpAsync())
                return;

            string resourceName = OperatingSystem.IsWindows() ? "VManager.Binaries.Windows.yt-dlp.exe"
                                 : OperatingSystem.IsLinux()   ? "VManager.Binaries.Linux.yt-dlp"
                                 : OperatingSystem.IsMacOS()   ? "VManager.Binaries.Mac.yt-dlp_macos"
                                 : throw new PlatformNotSupportedException();

            ExtractBinary(resourceName, targetFile);

            if (!OperatingSystem.IsWindows())
                Process.Start("chmod", $"+x \"{YtDlpPath}\"")?.WaitForExit();
        }
        finally
        {
            _extractLock.Release();
        }
    }

    private static async Task<bool> TestYtDlpAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = YtDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return false;

            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractBinary(string resourceName, string targetFile)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new Exception($"Recurso {resourceName} no encontrado");

        // Extracción atómica: temp + move, mismo criterio que en SoundManager
        string tempPath = Path.Combine(BinariesPath, $"{targetFile}.tmp_{Guid.NewGuid():N}");
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            stream.CopyTo(fs);
        }
        File.Move(tempPath, YtDlpPath, overwrite: true);
    }

    private static async Task TryAutoUpdateAsync()
    {
        var lockFilePath = Path.Combine(BinariesPath, "yt-dlp_update.lock");

        try
        {
            using var lockStream = new FileStream(lockFilePath, FileMode.CreateNew);

            var psi = new ProcessStartInfo
            {
                FileName = YtDlpPath,
                Arguments = "-U",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return;

            await p.WaitForExitAsync();
        }
        catch (IOException)
        {
            Console.WriteLine("[YTDLP] Otro proceso está actualizando.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[YTDLP] ERROR: " + ex.Message);
            ErrorService.Show(ex);
        }
        finally
        {
            try { File.Delete(lockFilePath); } catch { }
        }
    }
}