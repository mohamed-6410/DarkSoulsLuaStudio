using System.IO;

namespace DS1LuaStudio.App.Services;

/// <summary>
/// Minimal append-only log in the app data folder. Failures inside the editor or the decompiler
/// are worth keeping, but must never interrupt the user mid-edit.
/// </summary>
/// <remarks>
/// An exception raised inside a WPF input or layout handler is delivered again on every mouse move,
/// which filled the log with gigabytes of the same stack. Writes are therefore collapsed while a
/// run keeps failing the same way, and the file is trimmed once it passes <see cref="MaxBytes"/>.
/// </remarks>
public static class Log
{
    /// <summary>Trim the log once it grows past this.</summary>
    private const long MaxBytes = 4L * 1024 * 1024;

    /// <summary>Keep at most this many bytes of the old log when trimming.</summary>
    private const long KeepBytes = 1L * 1024 * 1024;

    private static readonly object Gate = new();

    private static string? _lastMessage;
    private static int _repeatCount;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DS1LuaStudio",
        "studio.log");

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (string.Equals(message, _lastMessage, StringComparison.Ordinal))
            {
                _repeatCount++;
                return;
            }

            if (_repeatCount > 0)
            {
                Append($"{_repeatCount} further identical entries were suppressed.{Environment.NewLine}");
                _repeatCount = 0;
            }

            _lastMessage = message;
            Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
        }
    }

    private static void Append(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            Trim();
            File.AppendAllText(LogPath, text);
        }
        catch
        {
            // logging must never be the reason something fails
        }
    }

    /// <summary>Drops the oldest lines so a runaway failure cannot fill the disk.</summary>
    private static void Trim()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length <= MaxBytes) return;

        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(-KeepBytes, SeekOrigin.End);

        // Start on a line boundary so the retained text is not cut mid-stack.
        int newline = stream.ReadByte();
        while (newline != -1 && newline != (int)'\n') newline = stream.ReadByte();

        var kept = new byte[stream.Length - stream.Position];
        stream.ReadExactly(kept, 0, kept.Length);

        File.WriteAllBytes(LogPath, kept);
    }
}