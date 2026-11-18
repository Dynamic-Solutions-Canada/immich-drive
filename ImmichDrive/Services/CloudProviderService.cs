using System.Runtime.InteropServices;
using System.Text;
using static ImmichDrive.Classes.CloudFilter.CfApi;

namespace ImmichDrive.Services;

/// <summary>
/// The live Cloud Files sync provider: connects the cfapi callback table to the sync root,
/// hydrates placeholders on demand by streaming originals from Immich, and keeps the drive
/// read-only by refusing deletes and renames of placeholders outside <c>Upload</c>. Holds the
/// connection for the lifetime of the resident app. See <c>docs/cloud-files.md</c> and
/// <c>docs/read-only.md</c>.
/// </summary>
public sealed class CloudProviderService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    // Transfer smaller aligned chunks so slow originals make visible progress before Windows'
    // Cloud Files request deadline, instead of waiting for a full megabyte per callback.
    private const int ChunkBytes = 256 << 10;

    private readonly ImmichClient _client;
    private long _connectionKey;
    private bool _connected;

    // Keep the delegates + table rooted so the GC can't collect them while the OS holds them.
    private CF_CALLBACK_REGISTRATION[]? _table;
    private CF_CALLBACK? _onFetchData;
    private CF_CALLBACK? _onCancelFetchData;
    private CF_CALLBACK? _onNotifyDelete;
    private CF_CALLBACK? _onNotifyRename;

    // The sync root and its Upload folder as volume-relative paths ("\Users\me\ImmichDrive"), the
    // form the callbacks report, so a request can be matched without the drive letter.
    private string _rootPath = "";
    private string _uploadPrefix = "";

    /// <summary>This process: its own deletes and moves (prune, retire) are always allowed.</summary>
    private static readonly int OwnProcessId = Environment.ProcessId;

    /// <summary>
    /// Raised (on a thread-pool thread) when a hydration transfer fails, carrying the exception that
    /// caused it. <see cref="DriveManager"/> uses it to notice the server has gone away — a failed
    /// open is usually the first thing the user experiences when Immich stops answering.
    /// </summary>
    public event Action<Exception?>? TransferFailed;

    public CloudProviderService(ImmichClient client) => _client = client;

    public void Connect(string syncRootPath)
    {
        if (_connected)
        {
            return;
        }

        _rootPath = VolumeRelative(syncRootPath);
        _uploadPrefix = _rootPath + "\\" + UploadService.UploadFolderName + "\\";

        _onFetchData = OnFetchData;
        _onCancelFetchData = OnCancelFetchData;
        _onNotifyDelete = OnNotifyDelete;
        _onNotifyRename = OnNotifyRename;
        _table =
        [
            new CF_CALLBACK_REGISTRATION { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, Callback = _onFetchData },
            new CF_CALLBACK_REGISTRATION { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, Callback = _onCancelFetchData },
            new CF_CALLBACK_REGISTRATION { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, Callback = _onNotifyDelete },
            new CF_CALLBACK_REGISTRATION { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, Callback = _onNotifyRename },
            new CF_CALLBACK_REGISTRATION { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NONE, Callback = null! },
        ];

        int hr = CfConnectSyncRoot(
            syncRootPath, _table, IntPtr.Zero,
            CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO | CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH,
            out _connectionKey);

        if (hr < 0)
        {
            Logger.Error("CfConnectSyncRoot failed: 0x{0:X8}", hr);
            throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"CfConnectSyncRoot 0x{hr:X8}");
        }
        _connected = true;
        Logger.Info("Connected sync root at {0}", syncRootPath);
    }

    public void Disconnect()
    {
        if (!_connected)
        {
            return;
        }

        CfDisconnectSyncRoot(_connectionKey);
        _connected = false;
        _table = null;
        _onFetchData = null;
        _onCancelFetchData = null;
        _onNotifyDelete = null;
        _onNotifyRename = null;
    }

    // ── Callbacks (invoked on OS threads) ───────────────────────────
    private void OnFetchData(IntPtr callbackInfoPtr, IntPtr callbackParamsPtr)
    {
        try
        {
            var info = Marshal.PtrToStructure<CF_CALLBACK_INFO>(callbackInfoPtr);
            var fetch = Marshal.PtrToStructure<CF_CALLBACK_PARAMETERS_FETCHDATA>(callbackParamsPtr);

            string assetId = ReadFileIdentity(info);
            long connKey = info.ConnectionKey;
            long transferKey = info.TransferKey;
            long requestKey = info.RequestKey;
            long offset = fetch.RequiredFileOffset;
            long length = fetch.RequiredLength;

            // Offload the actual transfer; the callback itself should return promptly.
            _ = Task.Run(() => HydrateAsync(assetId, connKey, transferKey, requestKey, offset, length));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "OnFetchData failed");
        }
    }

    private void OnCancelFetchData(IntPtr callbackInfoPtr, IntPtr callbackParamsPtr)
    {
        // Cooperative cancellation hook — a production build would signal the matching
        // in-flight transfer (keyed by TransferKey) to stop. Left as a no-op stub.
    }

    // ── Read-only: deletes and renames ──────────────────────────────
    // Windows blocks the user's delete or rename of a placeholder until the provider answers, and
    // the answer's completion status allows or refuses it (CF_CALLBACK_TYPE_NOTIFY_DELETE and
    // NOTIFY_RENAME, acknowledged with CfExecute ACK_DELETE / ACK_RENAME). This is what keeps the
    // drive read-only while ImmichDrive runs; nothing permanent sits on the folder that would stop
    // Windows cleaning up after an uninstall. Every request gets an answer, or the user's app
    // waits for the 60 second callback timeout.

    /// <summary>
    /// A placeholder is about to be deleted: allowed for this process's own housekeeping and inside
    /// Upload, refused everywhere else.
    /// </summary>
    private void OnNotifyDelete(IntPtr callbackInfoPtr, IntPtr callbackParamsPtr)
    {
        CF_CALLBACK_INFO info = default;
        bool allow = false;
        try
        {
            info = Marshal.PtrToStructure<CF_CALLBACK_INFO>(callbackInfoPtr);
            allow = IsOwnProcess(info) || IsInUpload(ReadPath(info.NormalizedPath));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "OnNotifyDelete failed; refusing the delete");
        }

        Acknowledge(CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE, info, allow);
    }

    /// <summary>
    /// A placeholder is about to be renamed or moved: allowed for this process, and for a move that
    /// stays inside Upload; refused everywhere else, including moves out of the drive.
    /// </summary>
    private void OnNotifyRename(IntPtr callbackInfoPtr, IntPtr callbackParamsPtr)
    {
        CF_CALLBACK_INFO info = default;
        bool allow = false;
        try
        {
            info = Marshal.PtrToStructure<CF_CALLBACK_INFO>(callbackInfoPtr);
            var rename = Marshal.PtrToStructure<CF_CALLBACK_PARAMETERS_RENAME>(callbackParamsPtr);
            bool targetInScope = (rename.Flags & CF_CALLBACK_RENAME_FLAG_TARGET_IN_SCOPE) != 0;
            allow = IsOwnProcess(info) ||
                (targetInScope && IsInUpload(ReadPath(info.NormalizedPath)) && IsInUpload(ReadPath(rename.TargetPath)));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "OnNotifyRename failed; refusing the rename");
        }

        Acknowledge(CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME, info, allow);
    }

    /// <summary>Answers a delete or rename request: STATUS_SUCCESS lets it happen, STATUS_ACCESS_DENIED refuses it.</summary>
    private static void Acknowledge(CF_OPERATION_TYPE type, CF_CALLBACK_INFO info, bool allow)
    {
        var opInfo = new CF_OPERATION_INFO
        {
            StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
            Type = type,
            ConnectionKey = info.ConnectionKey,
            TransferKey = info.TransferKey,
            RequestKey = info.RequestKey,
        };
        var opParams = new CF_OPERATION_PARAMETERS_ACK
        {
            ParamSize = (uint)Marshal.SizeOf<CF_OPERATION_PARAMETERS_ACK>(),
            CompletionStatus = allow ? STATUS_SUCCESS : STATUS_ACCESS_DENIED,
        };
        int hr = CfExecute(in opInfo, ref opParams);
        if (hr < 0)
        {
            Logger.Warn("CfExecute({0}) 0x{1:X8}", type, hr);
        }
    }

    /// <summary>True when the request comes from this process (needs CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO).</summary>
    private static bool IsOwnProcess(CF_CALLBACK_INFO info) =>
        info.ProcessInfo != IntPtr.Zero &&
        Marshal.ReadInt32(info.ProcessInfo, CF_PROCESS_INFO_PROCESS_ID_OFFSET) == OwnProcessId;

    /// <summary>True for a path strictly inside the drive's Upload folder (not the folder itself).</summary>
    private bool IsInUpload(string path) =>
        _uploadPrefix.Length > 0 && VolumeRelative(path).StartsWith(_uploadPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A native path string, or empty for a null pointer.</summary>
    private static string ReadPath(IntPtr path) => path == IntPtr.Zero ? "" : Marshal.PtrToStringUni(path) ?? "";

    /// <summary>
    /// A path without its drive letter or <c>\\?\</c> prefix ("\\Users\\me\\ImmichDrive"), the form the
    /// callbacks report; paths that are already volume-relative pass through unchanged.
    /// </summary>
    private static string VolumeRelative(string path)
    {
        string p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            p = p[4..];
        }

        if (p.Length >= 2 && p[1] == ':')
        {
            p = p[2..];
        }

        return p.TrimEnd('\\');
    }

    private async Task HydrateAsync(string assetId, long connKey, long transferKey, long requestKey, long offset, long length)
    {
        if (string.IsNullOrEmpty(assetId))
        {
            Logger.Warn("Hydrate with empty asset id; failing fetch");
            TransferData(connKey, transferKey, requestKey, IntPtr.Zero, offset, 0, STATUS_UNSUCCESSFUL);
            return;
        }

        var buffer = new byte[ChunkBytes];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            using var resp = await _client.GetOriginalAsync(assetId, offset, length);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync();

            long pos = offset;
            long end = offset + length;
            while (pos < end)
            {
                int want = (int)Math.Min(ChunkBytes, end - pos);
                int read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, want), want, throwOnEndOfStream: false);
                if (read <= 0)
                {
                    break;
                }

                TransferData(connKey, transferKey, requestKey, handle.AddrOfPinnedObject(), pos, read, STATUS_SUCCESS);
                pos += read;
            }
            if (pos < end) // server returned less than promised
            {
                TransferData(connKey, transferKey, requestKey, handle.AddrOfPinnedObject(), pos, 0, STATUS_UNSUCCESSFUL);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Hydration failed for asset {0}", assetId);
            // Tell the OS the fetch failed so the open returns an error instead of hanging.
            TransferData(connKey, transferKey, requestKey, handle.AddrOfPinnedObject(), offset, 0, STATUS_UNSUCCESSFUL);
            try { TransferFailed?.Invoke(ex); } catch { /* never let a listener break the callback */ }
        }
        finally
        {
            handle.Free();
        }
    }

    private static void TransferData(long connKey, long transferKey, long requestKey, IntPtr buffer, long offset, int length, int status)
    {
        var opInfo = new CF_OPERATION_INFO
        {
            StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
            Type = CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA,
            ConnectionKey = connKey,
            TransferKey = transferKey,
            RequestKey = requestKey,
        };
        var opParams = new CF_OPERATION_PARAMETERS_TRANSFERDATA
        {
            ParamSize = (uint)Marshal.SizeOf<CF_OPERATION_PARAMETERS_TRANSFERDATA>(),
            CompletionStatus = status,
            Buffer = buffer,
            Offset = offset,
            Length = length,
        };
        int hr = CfExecute(in opInfo, ref opParams);
        if (hr < 0)
        {
            Logger.Warn("CfExecute(TRANSFER_DATA) 0x{0:X8} at offset {1}", hr, offset);
        }
    }

    private static string ReadFileIdentity(CF_CALLBACK_INFO info)
    {
        // The identity is a short UTF-8 GUID string; bound the length defensively so a bad value
        // can never read protected memory.
        if (info.FileIdentity == IntPtr.Zero || info.FileIdentityLength == 0 || info.FileIdentityLength > 4096)
        {
            return "";
        }

        var bytes = new byte[info.FileIdentityLength];
        Marshal.Copy(info.FileIdentity, bytes, 0, bytes.Length);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    public void Dispose() => Disconnect();
}
