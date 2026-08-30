// Services/Core/Execution/ProcessSuspender.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VManager.Services.Core.Execution;

internal static class ProcessSuspender
{
    [DllImport("ntdll.dll")]
    [SupportedOSPlatform("windows")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    [SupportedOSPlatform("windows")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    public static bool Suspend(int pid)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var process = Process.GetProcessById(pid);
                return NtSuspendProcess(process.Handle) == 0;
            }
            else
            {
                return RunSignal(pid, "-STOP");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG]: No se pudo suspender PID {pid}: {ex.Message}");
            return false;
        }
    }

    public static bool Resume(int pid)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var process = Process.GetProcessById(pid);
                return NtResumeProcess(process.Handle) == 0;
            }
            else
            {
                return RunSignal(pid, "-CONT");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG]: No se pudo reanudar PID {pid}: {ex.Message}");
            return false;
        }
    }

    private static bool RunSignal(int pid, string signal)
    {
        using var kill = Process.Start(new ProcessStartInfo
        {
            FileName = "kill",
            Arguments = $"{signal} {pid}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        kill?.WaitForExit();
        return kill?.ExitCode == 0;
    }
}