using System.Text;
using WinUIAppKit.Settings;

namespace ImmichDrive.Services;

/// <summary>
/// ImmichDrive's settings for the kit's <see cref="ProtectedStore"/> (SEC-1, CONF-5). The folder,
/// file-name pattern and entropy are permanent: installed copies already keep the API key in
/// <c>protected-immich-api-key.dat</c> under these values, and changing any of them would make it
/// unreadable. WinUI-free and NLog-free: linked into the thumbnail provider and the test harness.
/// </summary>
public static class CredentialStore
{
    /// <summary>The file name for a stored name. Permanent.</summary>
    public const string FileNameFormat = "protected-{0}.dat";

    /// <summary>The DPAPI entropy string. Permanent.</summary>
    public const string EntropyText = "ImmichDrive.ProtectedStore.v1";

    /// <summary>
    /// Creates the store over the shared data folder that both the app and the thumbnail provider
    /// resolve through <see cref="SharedPaths"/>.
    /// </summary>
    /// <param name="folder">Overrides the folder; tests pass a temporary one.</param>
    /// <param name="protector">Overrides Windows data protection; tests pass a fake.</param>
    /// <param name="logError">Receives read and write failures; messages never contain the key.</param>
    public static ProtectedStore Create(
        Func<string>? folder = null,
        IDataProtector? protector = null,
        Action<string, Exception>? logError = null)
    {
        return new ProtectedStore(
            folder ?? (() => SharedPaths.DataDir),
            FileNameFormat,
            Encoding.UTF8.GetBytes(EntropyText),
            protector,
            logError);
    }
}
