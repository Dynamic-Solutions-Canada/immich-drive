using ImmichDrive.Services;

namespace ImmichDrive.ThumbnailProvider;

/// <summary>
/// The thumbnail provider's diagnostic log (FIX-063). The provider is NLog-free, so this is a small
/// best-effort file writer with three limits the old <c>C:\ProgramData\ImmichDrive\thumb.log</c> lacked:
/// <list type="bullet">
/// <item>It lives in the app's data folder (<see cref="SharedPaths.DataDir"/>), beside the app's own
/// logs, so "Open logs folder" shows it and uninstalling the Store app removes it.</item>
/// <item>It is capped: past <see cref="MaxBytes"/> the file moves to <c>.1</c>, replacing the previous
/// one, so it never holds more than about twice that.</item>
/// <item>Callers record failures only, by reason, error code or Immich asset id: never a file path,
/// album or file name, which would describe the user's library.</item>
/// </list>
/// Writing never throws; a provider that cannot log still draws thumbnails.
/// </summary>
internal static class ThumbnailLog
{
    /// <summary>The file name in the data folder.</summary>
    private const string FileName = "thumbnail-provider.log";

    /// <summary>Size at which the file is rotated.</summary>
    private const long MaxBytes = 256 * 1024;

    /// <summary>One writer at a time: the COM server can be called on several threads.</summary>
    private static readonly object Gate = new();

    /// <summary>
    /// Appends one line, stamped with the local time and its UTC offset.
    /// </summary>
    /// <param name="message">What failed and why, with no file path or name in it.</param>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                string path = Path.Combine(SharedPaths.DataDir, FileName);
                RotateIfFull(path);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}\r\n");
            }
        }
        catch
        {
            // Diagnostics only: a log that cannot be written must not cost the user a thumbnail.
        }
    }

    /// <summary>Moves a file at or past the cap to <c>.1</c>, replacing the previous one.</summary>
    private static void RotateIfFull(string path)
    {
        var file = new FileInfo(path);
        if (file.Exists && file.Length >= MaxBytes)
        {
            File.Move(path, path + ".1", overwrite: true);
        }
    }
}
