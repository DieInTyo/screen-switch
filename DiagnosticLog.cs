using System.Reflection;
using System.Runtime.InteropServices;

namespace ScreenSwitch;

internal static class DiagnosticLog
{
    private const long MaxLogBytes = 256 * 1024;
    private const long TrimmedLogBytes = 128 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenSwitch");
    private static readonly string LogPath = Path.Combine(DirectoryPath, "screen-switch.log");

    public static void Start()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        Info($"start pid={Environment.ProcessId} version={version}");
    }

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Win32Failure(string operation, IntPtr handle)
    {
        Write("WARN", $"{operation} hwnd={FormatHandle(handle)} error={Marshal.GetLastWin32Error()}");
    }

    public static string FormatHandle(IntPtr handle)
    {
        return $"0x{handle.ToInt64():X}";
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(DirectoryPath);
                TrimIfNeeded();
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never affect window movement.
        }
    }

    private static void TrimIfNeeded()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length <= MaxLogBytes)
        {
            return;
        }

        byte[] buffer;
        using (var stream = File.Open(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var bytesToKeep = (int)Math.Min(TrimmedLogBytes, stream.Length);
            stream.Seek(-bytesToKeep, SeekOrigin.End);

            buffer = new byte[bytesToKeep];
            _ = stream.Read(buffer, 0, buffer.Length);
        }

        File.WriteAllBytes(LogPath, buffer);
    }
}
