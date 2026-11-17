using ImmichDrive.Services;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImmichDrive.ThumbnailProvider;

/// <summary>
/// In-process COM thumbnail handler for ImmichDrive placeholder files. Resolves the file path
/// → Immich asset id via the shared SQLite index and fetches Immich's small thumbnail over HTTP
/// — without ever opening (hydrating) the file. Runs inside the shell, so it stays lean and
/// fails fast/quiet. See <c>docs/thumbnails.md</c>.
/// </summary>
[ComVisible(true)]
[Guid(ComGuids.ThumbnailProviderClsid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ImmichThumbnailProvider : IThumbnailProvider, IInitializeWithItem
{
    // HRESULT values keep their Win32 names so they read the same as the COM documentation.
#pragma warning disable IDE1006
    private const int S_OK = 0;
    private const int E_FAIL = unchecked((int)0x80004005);
#pragma warning restore IDE1006
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);

    private string? _path;

    public int Initialize(IShellItem psi, uint grfMode)
    {
        try
        {
            // SIGDN_FILESYSPATH gives the path without opening a stream (no hydration).
            int hr = psi.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr p);
            if (hr < 0 || p == IntPtr.Zero) { ThumbnailLog.Write($"Initialize: no file path, hr=0x{hr:X8}"); return E_FAIL; }
            try { _path = Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
            return string.IsNullOrEmpty(_path) ? E_FAIL : S_OK;
        }
        catch (Exception ex) { ThumbnailLog.Write($"Initialize failed: {Describe(ex)}"); return E_FAIL; }
    }

    public int GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
    {
        phbmp = IntPtr.Zero;
        pdwAlpha = WTS_ALPHATYPE.WTSAT_RGB;

        try
        {
            if (string.IsNullOrEmpty(_path)) { ThumbnailLog.Write("GetThumbnail: no path"); return E_FAIL; }

            // Only handle files inside the sync root; otherwise let the shell's default handler run.
            string syncRoot = SettingsFile.ReadSyncRoot();
            string full = Path.GetFullPath(_path);
            if (!full.StartsWith(EnsureSep(syncRoot), StringComparison.OrdinalIgnoreCase))
            {
                // Not ours: the shell's default handler takes it. Normal, so not logged.
                return E_FAIL;
            }

            string rel = full[EnsureSep(syncRoot).Length..];
            string? assetId = new AssetIndex().TryGetAssetId(rel);
            if (string.IsNullOrEmpty(assetId)) { ThumbnailLog.Write("GetThumbnail: the file is not in the index"); return E_FAIL; }

            var conn = SettingsFile.ReadConnection();
            if (conn is not { } c) { ThumbnailLog.Write("GetThumbnail: no connection in settings"); return E_FAIL; }

            byte[]? bytes = FetchThumbnail(c.ServerUrl, c.ApiKey, assetId);
            if (bytes is null || bytes.Length == 0) { ThumbnailLog.Write($"GetThumbnail: empty thumbnail for asset {assetId}"); return E_FAIL; }

            phbmp = ToHBitmap(bytes, (int)cx);
            if (phbmp == IntPtr.Zero)
            {
                ThumbnailLog.Write($"GetThumbnail: could not decode the thumbnail for asset {assetId}");
                return E_FAIL;
            }

            return S_OK;
        }
        catch (Exception ex) { ThumbnailLog.Write($"GetThumbnail failed: {Describe(ex)}"); return E_FAIL; }
    }

    /// <summary>
    /// An exception for the log: its type and HRESULT only. Messages can carry file paths or server
    /// details, so they are left out (FIX-063).
    /// </summary>
    private static string Describe(Exception ex) => $"{ex.GetType().Name} 0x{ex.HResult:X8}";

    private static byte[]? FetchThumbnail(string url, string key, string assetId)
    {
        using var client = new ImmichClient(url, key, HttpTimeout);
        // Block briefly; the shell calls us on a worker thread. preview = JPEG (decodable by GDI+).
        return Task.Run(() => client.GetThumbnailBytesAsync(assetId, "preview")).GetAwaiter().GetResult();
    }

    private static IntPtr ToHBitmap(byte[] bytes, int cx)
    {
        using var src = (Bitmap)Image.FromStream(new MemoryStream(bytes));
        double ratio = Math.Min((double)cx / src.Width, (double)cx / src.Height);
        if (ratio <= 0 || double.IsInfinity(ratio))
        {
            ratio = 1;
        }

        int w = Math.Max(1, (int)Math.Round(src.Width * ratio));
        int h = Math.Max(1, (int)Math.Round(src.Height * ratio));

        using var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
        }
        // The shell takes ownership of the returned HBITMAP and deletes it.
        return dst.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
    }

    private static string EnsureSep(string dir) =>
        dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
}
