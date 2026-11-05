using System.Runtime.InteropServices;
using ImmichDrive.Classes.CloudFilter;

namespace ImmichDrive.Tests.CloudFilterLayout;

internal static class Program
{
    /// <summary>
    /// Checks the production operation structure against the 64-bit cfapi.h ABI.
    /// Returns a nonzero exit code for unsupported architectures or a layout mismatch.
    /// </summary>
    private static int Main()
    {
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Console.Error.WriteLine("Run this check on x64 or ARM64, the app's shipping architectures.");
            return 1;
        }

        // Native CF_OPERATION_INFO: two 32-bit fields, two 64-bit keys, two
        // pointers, then the 64-bit request key. These offsets come from cfapi.h.
        (string Name, int Offset)[] fields =
        [
            (nameof(CfApi.CF_OPERATION_INFO.StructSize), 0),
            (nameof(CfApi.CF_OPERATION_INFO.Type), 4),
            (nameof(CfApi.CF_OPERATION_INFO.ConnectionKey), 8),
            (nameof(CfApi.CF_OPERATION_INFO.TransferKey), 16),
            (nameof(CfApi.CF_OPERATION_INFO.CorrelationVector), 24),
            (nameof(CfApi.CF_OPERATION_INFO.SyncStatus), 32),
            (nameof(CfApi.CF_OPERATION_INFO.RequestKey), 40),
        ];

        bool valid = Marshal.SizeOf<CfApi.CF_OPERATION_INFO>() == 48;
        foreach (var (name, expectedOffset) in fields)
        {
            int actualOffset = Marshal.OffsetOf<CfApi.CF_OPERATION_INFO>(name).ToInt32();
            if (actualOffset == expectedOffset)
            {
                continue;
            }

            Console.Error.WriteLine($"{name}: expected offset {expectedOffset}, got {actualOffset}.");
            valid = false;
        }

        Console.WriteLine($"CF_OPERATION_INFO ({RuntimeInformation.ProcessArchitecture}): {(valid ? "PASS" : "FAIL")}, size {Marshal.SizeOf<CfApi.CF_OPERATION_INFO>()} (expected 48).");
        return valid ? 0 : 1;
    }
}
