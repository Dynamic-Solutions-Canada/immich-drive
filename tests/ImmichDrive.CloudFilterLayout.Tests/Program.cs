using System.Runtime.InteropServices;
using ImmichDrive.Classes.CloudFilter;

namespace ImmichDrive.CloudFilterLayout.Tests;

internal static class Program
{
    /// <summary>
    /// Checks the production cfapi structures against the 64-bit cfapi.h ABI: field offsets and total
    /// size for each. Returns a nonzero exit code for unsupported architectures or any layout mismatch.
    /// </summary>
    private static int Main()
    {
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Console.Error.WriteLine("Run this check on x64 or ARM64, the app's shipping architectures.");
            return 1;
        }

        bool valid = true;

        // Native CF_OPERATION_INFO: two 32-bit fields, two 64-bit keys, two pointers, then the
        // 64-bit request key. These offsets come from cfapi.h.
        valid &= Check<CfApi.CF_OPERATION_INFO>(48,
        [
            (nameof(CfApi.CF_OPERATION_INFO.StructSize), 0),
            (nameof(CfApi.CF_OPERATION_INFO.Type), 4),
            (nameof(CfApi.CF_OPERATION_INFO.ConnectionKey), 8),
            (nameof(CfApi.CF_OPERATION_INFO.TransferKey), 16),
            (nameof(CfApi.CF_OPERATION_INFO.CorrelationVector), 24),
            (nameof(CfApi.CF_OPERATION_INFO.SyncStatus), 32),
            (nameof(CfApi.CF_OPERATION_INFO.RequestKey), 40),
        ]);

        // CF_OPERATION_PARAMETERS.AckDelete / AckRename: ParamSize, the union's 8-byte alignment, then
        // the arm's Flags and NTSTATUS CompletionStatus.
        valid &= Check<CfApi.CF_OPERATION_PARAMETERS_ACK>(16,
        [
            (nameof(CfApi.CF_OPERATION_PARAMETERS_ACK.ParamSize), 0),
            (nameof(CfApi.CF_OPERATION_PARAMETERS_ACK.Flags), 8),
            (nameof(CfApi.CF_OPERATION_PARAMETERS_ACK.CompletionStatus), 12),
        ]);

        // CF_CALLBACK_PARAMETERS.Delete: Flags at the start of the 8-byte-aligned arm.
        valid &= Check<CfApi.CF_CALLBACK_PARAMETERS_DELETE>(12,
        [
            (nameof(CfApi.CF_CALLBACK_PARAMETERS_DELETE.ParamSize), 0),
            (nameof(CfApi.CF_CALLBACK_PARAMETERS_DELETE.Flags), 8),
        ]);

        // CF_CALLBACK_PARAMETERS.Rename: Flags, then the target path pointer aligned to 8.
        valid &= Check<CfApi.CF_CALLBACK_PARAMETERS_RENAME>(24,
        [
            (nameof(CfApi.CF_CALLBACK_PARAMETERS_RENAME.ParamSize), 0),
            (nameof(CfApi.CF_CALLBACK_PARAMETERS_RENAME.Flags), 8),
            (nameof(CfApi.CF_CALLBACK_PARAMETERS_RENAME.TargetPath), 16),
        ]);

        return valid ? 0 : 1;
    }

    /// <summary>
    /// Compares one structure's managed layout with its native offsets and size, and prints one
    /// PASS or FAIL line with the details of any mismatch.
    /// </summary>
    /// <param name="expectedSize">The native sizeof on 64-bit Windows.</param>
    /// <param name="fields">Each field's name and its native offset.</param>
    /// <returns>True when every offset and the size match.</returns>
    private static bool Check<T>(int expectedSize, (string Name, int Offset)[] fields) where T : struct
    {
        bool valid = Marshal.SizeOf<T>() == expectedSize;
        foreach (var (name, expectedOffset) in fields)
        {
            int actualOffset = Marshal.OffsetOf<T>(name).ToInt32();
            if (actualOffset != expectedOffset)
            {
                Console.Error.WriteLine($"{typeof(T).Name}.{name}: expected offset {expectedOffset}, got {actualOffset}.");
                valid = false;
            }
        }

        Console.WriteLine($"{typeof(T).Name} ({RuntimeInformation.ProcessArchitecture}): {(valid ? "PASS" : "FAIL")}, size {Marshal.SizeOf<T>()} (expected {expectedSize}).");
        return valid;
    }
}
