---
name: commit
description: Review, document and commit ImmichDrive changes the user asked to commit. Use when asked to create a commit in this repo. Adds this repo's documentation check to the usual commit steps.
---

# Commit changes

Adds to a generic commit workflow: the docs this repo keeps in step with the code are updated in the same commit.

1. Run `git status` and `git diff` to review what changed, and confirm every path belongs in this commit.
2. Check the "Keeping docs current" table in `AGENTS.md`. If the change touches an area a doc or guide covers, update it in the same commit.
3. Run the checks that match the change (see "Commands" in `AGENTS.md`).
4. Stage the intended paths explicitly, then commit with an imperative subject and a body that explains why when the reason is not obvious.

Report: the commit hash and subject, the docs updated, and the checks run with their results.
