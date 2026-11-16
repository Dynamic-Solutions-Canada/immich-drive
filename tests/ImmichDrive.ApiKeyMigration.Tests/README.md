# API key migration harness

Checks the one-time move of the Immich API key from `settings.json` into the protected store, and `ApiKeyStore`'s rule that a key is never lost while that store is failing. It links the production `ApiKeyMigration`, `ApiKeyStore`, `ProtectedStore`, `AtomicFile`, `SharedPaths` and `StoredApiKey`, and replaces Windows data protection with a reversible fake.

Covers: key present or absent in each place, which key wins when both exist, store write and read-back failures, a `settings.json` rewrite that fails, an unreadable store, running twice, and saving a key while a failed migration is pending.

Does not prove: that Windows data protection itself works, that the thumbnail provider can decrypt the key in the shell, or anything about the installed package. Those need the manual upgrade check in `docs/development.md`.

Run with `npm test`, or alone with `scripts/dotnet run --project tests/ImmichDrive.ApiKeyMigration.Tests`. Run it as a non-root user: one case makes a folder read-only, which root ignores.
