using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ImmichDrive.Services;

/// <summary>
/// Small objects persisted outside settings.json, encrypted to the current Windows user.
/// </summary>
/// <remarks>
/// <para><b>Every credential belongs here, not in settings.json.</b> That file is plain text that
/// users copy, back up and attach to support requests, so a key inside it leaks with it.</para>
/// <para><see cref="ProtectedData"/> (DPAPI) ties the ciphertext to the Windows user account, so a
/// copied file is useless on another machine or under another user. That is the appropriate bar
/// for a desktop app: it defends against the file being read or moved, not against malware already
/// running as this user, which nothing at this layer can.</para>
/// <para>Shape copied from Little Launcher's <c>ProtectedStore</c> (same type and member names) so
/// the winui-app-kit version can replace it. Differences, all deliberate:</para>
/// <list type="bullet">
/// <item>WinUI-free and NLog-free, because the out-of-process thumbnail extension links it to read
/// the API key. Failures go to <see cref="LogError"/>, which the app wires to NLog.</item>
/// <item><see cref="Load{T}"/> never deletes an unreadable file. Two processes read it; deleting on
/// a transient failure would silently disconnect the drive. The app instead tells the user the
/// saved key could not be read (see <see cref="Exists"/>).</item>
/// <item><see cref="Save{T}"/> writes atomically, so the thumbnail extension never reads half a file.</item>
/// <item>Encryption sits behind <see cref="IDataProtector"/> so the portable test harness can run
/// the migration on Linux.</item>
/// </list>
/// </remarks>
internal static class ProtectedStore
{
    /// <summary>Extra entropy, so the blob cannot be decrypted by another app's DPAPI call alone.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ImmichDrive.ProtectedStore.v1");

    /// <summary>The encryption used. Tests replace it; production code never changes it.</summary>
    internal static IDataProtector Protector { get; set; } = new DpapiDataProtector(Entropy);

    /// <summary>The folder holding the encrypted files. Tests point it at a temporary folder.</summary>
    internal static Func<string> DirectoryResolver { get; set; } = () => SharedPaths.DataDir;

    /// <summary>
    /// Receives a short description and the exception when a read or write fails. Messages never
    /// contain the stored value. Null in the thumbnail extension, which has nowhere useful to log.
    /// </summary>
    internal static Action<string, Exception>? LogError { get; set; }

    private static string PathFor(string name) =>
        Path.Combine(DirectoryResolver(), $"protected-{name}.dat");

    /// <summary>Whether a stored file exists, readable or not. Lets callers tell "never saved" from "unreadable".</summary>
    public static bool Exists(string name)
    {
        try
        {
            return File.Exists(PathFor(name));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Load a stored object, or null when absent or unreadable. An unreadable file (restored profile,
    /// different Windows user, corruption) is left in place; see the class remarks.
    /// </summary>
    public static T? Load<T>(string name) where T : class
    {
        string path = PathFor(name);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] plain = Protector.Unprotect(File.ReadAllBytes(path));
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex)
        {
            LogError?.Invoke($"Could not read protected store '{name}'; treating as absent", ex);
            return null;
        }
    }

    /// <summary>
    /// Persist an object, encrypted to the current user. Failures are reported to
    /// <see cref="LogError"/> and swallowed, as in Little Launcher; a caller that must know the
    /// value landed reads it back with <see cref="Load{T}"/>.
    /// </summary>
    public static void Save<T>(string name, T value)
    {
        try
        {
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
            byte[] cipher = Protector.Protect(plain);
            AtomicFile.WriteAllBytes(PathFor(name), cipher);
        }
        catch (Exception ex)
        {
            LogError?.Invoke($"Could not save protected store '{name}'", ex);
        }
    }

    /// <summary>Forget a stored object.</summary>
    public static void Clear(string name)
    {
        try
        {
            string path = PathFor(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            LogError?.Invoke($"Could not delete protected store '{name}'", ex);
        }
    }
}

/// <summary>Symmetric encryption for <see cref="ProtectedStore"/>, abstracted so tests can run off Windows.</summary>
internal interface IDataProtector
{
    byte[] Protect(byte[] plain);

    byte[] Unprotect(byte[] cipher);
}

/// <summary>Windows data protection (DPAPI) scoped to the current user.</summary>
internal sealed class DpapiDataProtector(byte[] entropy) : IDataProtector
{
    public byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
}
