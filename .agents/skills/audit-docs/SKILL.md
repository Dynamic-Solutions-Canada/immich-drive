---
name: audit-docs
description: Check ImmichDrive's agent files, guides and docs against the code and fix what is stale. Use before a release or after a significant change. Do not use for writing new documentation from scratch.
---

# Audit the docs

Goal: every path, command, setting and behaviour named in the docs matches the tree.

1. Check that every path in the "Guidance map" in `AGENTS.md`, and every repo path named in `.agents/` and `docs/`, exists.
2. Check that every `npm run` command named in `AGENTS.md`, the skills and the docs exists in `package.json`.
3. Walk each document against the code it governs (its "Governs" line):
   - `AGENTS.md`: architecture summary, linked files, commands.
   - `ARCHITECTURE.md`: the process diagram, the cfapi, thumbnail and index flows, and where data and the API key live.
   - `docs/cloud-files.md`: cfapi calls, callback registration, placeholder fields.
   - `docs/immich-api.md`: endpoint paths, parameters, auth header, response shapes.
   - `docs/thumbnails.md`: COM interfaces, registration, path to asset mapping.
   - `docs/read-only.md`: the deny rules and the Upload flow.
   - `docs/packaging.md`: manifest, identities, packaging script, workflows.
   - `.agents/guides/user-settings.md`: the settings table against `UserSettings.cs`.
   - `.agents/guides/ui-conventions.md` and `.agents/guides/pinvoke.md`: the conventions still match the code.
   - The skills in `.agents/skills/`: every step still works.
4. Search the files above for em dashes and remove any.
5. Fix anything stale.

Report: which documents changed and why, and anything found stale but left for the user to decide.
