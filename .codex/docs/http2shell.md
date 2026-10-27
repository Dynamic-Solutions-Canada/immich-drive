# HTTP2Shell assessment (CVE-2026-111497)

Reviewed October 26, 2026. This is a runtime build-floor correction, with exploit reachability unconfirmed.

## Project identity and baseline

The local `RyanEwen/ImmichDrive` origin redirects to `Dynamic-Solutions-Canada/immich-drive` (GitHub repository ID `1282688944`). A fresh fetch of canonical `main` matched the clean task branch at `9fe078176ffa68f8e2d7cb25b1887433c8f908d0`. No original branch or remote configuration was changed.

## Verified package provenance

The successful [v0.1.48 Store run](https://github.com/Dynamic-Solutions-Canada/immich-drive/actions/runs/37721900497), [publish job 113131217890](https://github.com/Dynamic-Solutions-Canada/immich-drive/actions/runs/37721900497/job/113131217890), used SDK 10.0.401 with runtime 10.0.12. The job built the app and thumbnail provider for both x64 and ARM64, packed `System.IO.Compression.Brotli.dll` and `System.IO.Compression.Native.dll`, uploaded the bundle, and reached certification for submission `1152921505702069812`. Package entries include `mscordaccore_amd64_amd64_10.0.1226.42308.dll` and the corresponding ARM64 file, corroborating the embedded older runtime. This is build/submission evidence, not confirmation of what is currently installed or publicly available through the Store.

`ImmichDriveMSIX/build-msix.ps1` publishes both executables self-contained, then merges their output. A shared-runtime update on the user's machine cannot replace these embedded files.

## Reachability and dependencies

All checked-in C# was searched for Brotli APIs, compression/decompression configuration, HTTP handlers, and HTTP clients. `ImmichClient` (also linked into the thumbnail executable) and `UpdateService` construct default `HttpClient` instances. Neither enables automatic decompression. No `BrotliStream`, `BrotliDecoder`, `AutomaticDecompression`, or request-decompression middleware was found.

The exact promotional submodule commit `23baa39543f0ebe15e3561c542d6849d9dbfc12a` was restored in this task worktree from [Dynamic-Solutions-Canada/apps-promo](https://github.com/Dynamic-Solutions-Canada/apps-promo/tree/23baa39543f0ebe15e3561c542d6849d9dbfc12a), without changing shared checkout configuration. Its imported project adds no package references. Its only linked C# source constructs a default HttpClient for manifest/icon downloads and contains no Brotli or decompression configuration.

The project references Windows App SDK, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, and NLog; the thumbnail executable references Microsoft.Data.Sqlite and the Windows desktop framework. No explicit Brotli package or third-party HTTP client is referenced. A source/package-reference inventory does not establish all transitive or native behavior. No attacker-controlled decoder path or working exploit has been established, so this project is not described as proven exploitable.

[Microsoft's advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-p3jp-9v83-76w9) fixes .NET 10 in runtime 10.0.14 and SDK 10.0.114 or 10.0.403. It confirms that default HttpClient behavior does not enable Brotli decompression and that exposure requires passing attacker-controlled Brotli data to a decoder. Self-contained applications need rebuilding and redistribution to replace their embedded runtime.

## Narrow correction

- `global.json` selects stable SDK 10.0.403 or a later installed .NET 10 feature band/patch. Both packaging workflows install that baseline via `actions/setup-dotnet`.
- `Directory.Build.targets` checks the runtime pack selected by `ProcessFrameworkReferences` for self-contained builds. It rejects .NET core runtime packs below 10.0.14, including explicit runtime-version overrides. It does not pin `RuntimeFrameworkVersion`, so later SDK security patches remain eligible. Both executables inherit the check.
- The build/package workflow exercises real project framework resolution for both architectures, checking default selection, rejection of 10.0.13, and acceptance of 10.0.14 plus a synthetic later version. This needs no package restore, Windows compiler, or app launch.
- App version, package identities, HTTP behavior, dependency versions, and distribution policy remain unchanged.

SDK selection and runtime pack selection are separate. Microsoft's [global.json documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) defines the SDK roll-forward policy. [RuntimeFrameworkVersion](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#runtimeframeworkversion) would select an exact runtime for a self-contained publish; the shared target instead validates the resolved pack to avoid freezing later updates.

## Verification and remaining gates

Static JSON/XML, workflow selection, ASCII script, unchanged app version, and whitespace checks pass.

- All 16 executable framework-resolution cases pass using `.github/scripts/tests/runtime-build-floor.Tests.ps1`: both real projects, both architectures, SDK default selection, rejection of explicit 10.0.13, and acceptance of 10.0.14 plus synthetic 10.0.15. Successful cases assert the selected core runtime pack's architecture/version and exclude Windows SDK reference packs. The later-version case proves selection policy, not availability of that future package.
- SDK selection checks reject cached SDK 10.0.402 and accept cached SDK 10.0.403 with Microsoft.NETCore.App 10.0.14.
- These checks used cached official images with no network, no restore, no compilation, one CPU, a 2 GiB memory limit, and the shared host validation lock. The patched image digest is `sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b`.
- Local logs are retained under `.artifacts/http2shell/`: `runtime-build-floor.txt`, `sdk-10.0.402.txt`, and `sdk-10.0.403.txt`. Disposable containers exited automatically; no task containers, volumes, or builders remain.

Full Windows Debug compilation and unsigned x64/ARM64 MSIX builds remain required and are outside the current capacity clearance. Inspect both published dependency/runtime manifests and the final merged layout for runtime 10.0.14 or newer, including native compression binaries. Metadata selection checks do not prove shipped package contents, transitive binary/native behavior, or Windows acceptance. No Windows application, desktop, or device was used for this task. No Store release or deployment was performed. Existing installations are unchanged.
