using System.IO;
using System.Text;

namespace ImmichDrive.Services;

/// <summary>
/// Replaces a file in one step, so a reader (or a crash mid-write) sees either the old file or
/// the new one, never a truncated mix. WinUI-free and NLog-free: linked into the thumbnail
/// extension and the portable test harness.
/// </summary>
internal static class AtomicFile
{
    /// <summary>How many times to retry the final rename when another process holds the target.</summary>
    private const int ReplaceAttempts = 3;

    /// <summary>Writes UTF-8 text atomically. See <see cref="WriteAllBytes"/>.</summary>
    public static void WriteAllText(string path, string contents) =>
        WriteAllBytes(path, Encoding.UTF8.GetBytes(contents));

    /// <summary>
    /// Writes <paramref name="bytes"/> to a temporary file beside <paramref name="path"/>, then
    /// renames it over the target. The temporary file sits in the same folder so the rename stays on
    /// one volume. On any failure the temporary file is deleted and the exception is rethrown;
    /// no backup copy is ever left behind.
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            ReplaceWithRetry(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Renames <paramref name="source"/> over <paramref name="target"/>. The thumbnail extension may
    /// be reading the target for a few milliseconds, which makes Windows refuse the replace, so a
    /// short retry covers that without masking a real failure.
    /// </summary>
    private static void ReplaceWithRetry(string source, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < ReplaceAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: a leftover .tmp holds only what was being written, never the old file.
        }
    }
}
