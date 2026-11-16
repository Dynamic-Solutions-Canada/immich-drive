// Portable harness for UploadService's pending/complete state on the Upload folder. It links the
// real UploadService and stubs ImmichClient, CloudFolderState and NLog below, so it runs on Linux.
// Exits nonzero (an exception) on the first failed check.
using System.Reflection;
using ImmichDrive.Services;

var root = Path.Combine(Path.GetTempPath(), "ImmichDrive-upload-status-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var client = new ImmichClient();
using var service = new UploadService(client, root);
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
void Refresh() => typeof(UploadService).GetMethod("RefreshFolderSyncState", flags)!.Invoke(service, null);
void Enqueue(string path) => typeof(UploadService).GetMethod("Enqueue", flags)!.Invoke(service, [path]);
Task Process() => (Task)typeof(UploadService).GetMethod("ProcessPendingAsync", flags)!.Invoke(service, null)!;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
}
try
{
    Refresh();
    Check(CloudFolderState.Last == true, "empty folder reports complete");
    var calls = CloudFolderState.Calls;
    Refresh();
    Check(CloudFolderState.Calls == calls, "idle folder avoids repeated shell updates");
    var file = Path.Combine(root, "photo.jpg");
    File.WriteAllText(file, "photo");
    Enqueue(file);
    client.Upload = _ => Task.FromResult(false);
    await Process();
    Check(CloudFolderState.Last == false && File.Exists(file), "failed upload stays pending after queue drains");
    File.Delete(file);
    Enqueue(file);
    await Process();
    Check(CloudFolderState.Last == true, "external deletion clears pending state");
    File.WriteAllText(file, "photo");
    Enqueue(file);
    var completion = new TaskCompletionSource<bool>();
    client.Upload = _ => completion.Task;
    var processing = Process();
    Check(CloudFolderState.Last == false, "in-flight upload reports pending before HTTP finishes");
    completion.SetResult(true);
    await processing;
    Check(CloudFolderState.Last == true && !File.Exists(file), "successful upload clears pending after local deletion");
    File.WriteAllText(file, "photo");
    Enqueue(file);
    using (var held = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        await Process();
        Check(CloudFolderState.Last == false, "unfinished copy remains pending");
    }
    client.Upload = _ => throw new IOException("simulated upload failure");
    await Process();
    Check(CloudFolderState.Last == false, "thrown upload failure remains pending");
    File.Delete(file);
    Directory.CreateDirectory(Path.Combine(root, "empty", "nested"));
    await Process();
    Check(CloudFolderState.Last == true, "empty nested folders do not count as uploads");
    Directory.CreateDirectory(Path.Combine(root, "nested"));
    var nested = Path.Combine(root, "nested", "photo.jpg");
    File.WriteAllText(nested, "photo");
    Refresh();
    Check(CloudFolderState.Last == false, "nested leftover files remain pending even without a watcher event");
    File.Delete(nested);
    CloudFolderState.Succeed = false;
    Refresh();
    calls = CloudFolderState.Calls;
    CloudFolderState.Succeed = true;
    Refresh();
    Check(CloudFolderState.Calls == calls + 1 && CloudFolderState.Last == true, "failed Cloud Files update retries");
    Directory.Delete(Path.Combine(root, "nested"));
    Directory.Delete(root);
    Refresh();
    Check(CloudFolderState.Last == false, "enumeration failure never reports complete");

    Directory.CreateDirectory(root);
    using var watched = new UploadService(client, root);
    watched.Start();
    Check(CloudFolderState.Last == true, "startup reports an existing empty folder complete");
    File.WriteAllText(file, "photo");
    await Task.Delay(2500);
    Check(CloudFolderState.Last == false, "real watcher observes drops");
    File.Delete(file);
    await Task.Delay(2500);
    Check(CloudFolderState.Last == true, "real watcher observes removals");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

namespace ImmichDrive.Services
{
    public sealed class ImmichClient
    {
        public Func<string, Task<bool>> Upload = _ => Task.FromResult(true);
        public Task<bool> UploadAssetAsync(string path) => Upload(path);
    }
    internal static class CloudFolderState
    {
        public static bool? Last;
        public static int Calls;
        public static bool Succeed = true;
        public static bool SetInSync(string path, bool inSync)
        {
            Calls++;
            if (Succeed) Last = inSync;
            return Succeed;
        }
    }
}
namespace NLog
{
    public class Logger
    {
        public void Warn(Exception ex, string message, params object[] args) { }
        public void Warn(string message, params object[] args) { }
        public void Info(string message, params object[] args) { }
    }
    public static class LogManager
    {
        public static Logger GetCurrentClassLogger() => new();
    }
}
