using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace VManager.Services;

public static class DenoManager
{
    public static string DenoPath { get; private set; } = string.Empty;

    // Carpeta persistente, hermana de "Themes"
    private static string AppRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    private static string BinariesPath => Path.Combine(AppRoot, "Binaries");

    private static readonly SemaphoreSlim _extractLock = new(1, 1);

    private static Task? _initializeTask;
    private static readonly object InitLock = new();

    /// <summary>Inicializa Deno bajo demanda (p. ej. al abrir VDownload).</summary>
    public static Task EnsureInitialized()
    {
        if (_initializeTask != null)
            return _initializeTask;

        lock (InitLock)
            return _initializeTask ??= Initialize();
    }

    public static async Task Initialize()
    {
        string targetFile = OperatingSystem.IsWindows() ? "deno.exe"
                          : OperatingSystem.IsMacOS() ? "deno_macos"
                          : "deno";

        Directory.CreateDirectory(BinariesPath);
        DenoPath = Path.Combine(BinariesPath, targetFile);

        if (File.Exists(DenoPath) && !OperatingSystem.IsWindows())
            Process.Start("chmod", $"+x \"{DenoPath}\"")?.WaitForExit();

        bool needsExtract = !File.Exists(DenoPath) || !await TestDenoAsync();

        if (needsExtract)
        {
            Console.WriteLine("[DENO] Extrayendo versión embebida…");
            await ExtractForOS(targetFile);
        }

        Console.WriteLine($"[DENO] path: {DenoPath}");

        _ = TryAutoUpdateAsync();
    }

    private static async Task ExtractForOS(string targetFile)
    {
        await _extractLock.WaitAsync();
        try
        {
            // Doble chequeo por si otro hilo ya extrajo mientras esperábamos
            if (File.Exists(DenoPath) && await TestDenoAsync())
                return;

            string resourceName = OperatingSystem.IsWindows() ? "VManager.Binaries.Windows.deno.exe"
                                 : OperatingSystem.IsLinux()   ? "VManager.Binaries.Linux.deno"
                                 : OperatingSystem.IsMacOS()   ? "VManager.Binaries.Mac.deno"
                                 : throw new PlatformNotSupportedException();

            ExtractBinary(resourceName, targetFile);

            if (!OperatingSystem.IsWindows())
                Process.Start("chmod", $"+x \"{DenoPath}\"")?.WaitForExit();
        }
        finally
        {
            _extractLock.Release();
        }
    }

    private static async Task<bool> TestDenoAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = DenoPath,
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

        string tempPath = Path.Combine(BinariesPath, $"{targetFile}.tmp_{Guid.NewGuid():N}");
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            stream.CopyTo(fs);
        }
        File.Move(tempPath, DenoPath, overwrite: true);
    }

    private static async Task TryAutoUpdateAsync()
    {
        var lockFilePath = Path.Combine(BinariesPath, "deno_update.lock");

        try
        {
            using var lockStream = new FileStream(lockFilePath, FileMode.CreateNew);

            var psi = new ProcessStartInfo
            {
                FileName = DenoPath,
                Arguments = "upgrade",
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
            Console.WriteLine("[DENO] Otro proceso está actualizando.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DENO] ERROR: " + ex.Message);
            ErrorService.Show(ex);
        }
        finally
        {
            try { File.Delete(lockFilePath); } catch { }
        }
    }
}