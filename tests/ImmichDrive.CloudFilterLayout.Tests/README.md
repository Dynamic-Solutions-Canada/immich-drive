# Cloud Files structure layout harness

Checks that the production `CF_OPERATION_INFO` declaration in `CfApi.cs` has the size and field offsets of the 64-bit `cfapi.h` structure, including the request key after both pointers. A wrong offset silently corrupts hydration.

Covers: the layout on the architecture it runs on. `npm test` runs it on Linux x64, which shares the Windows x64 layout; the release workflows run it natively on Windows.

Does not prove: the ARM64 layout unless run on ARM64, or anything about calling Cloud Files; it never calls the API or touches a sync root.

Run with `npm test`, or alone with `scripts/dotnet run --project tests/ImmichDrive.CloudFilterLayout.Tests`.
