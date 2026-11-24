> **Scope:** Removing a drive folder left behind by ImmichDrive 0.1.50 or earlier. Linked from the README's Uninstall section.
> **Governs:** Nothing in code; what uninstall leaves behind is in ARCHITECTURE.md, "Where the app writes, and what uninstall leaves".

# Removing a folder left by 0.1.50 or earlier

If you uninstalled ImmichDrive 0.1.50 or earlier without disconnecting first, the drive folder may stay behind: its photos can't be opened ("The cloud file provider is not running") and Windows refuses to delete it ("Access is denied"). To remove it, open a Command Prompt (no administrator rights needed) and run:

```
icacls "%USERPROFILE%\ImmichDrive" /reset /t /c /q
attrib -r "%USERPROFILE%\ImmichDrive\*" /s /d
rmdir /s /q "%USERPROFILE%\ImmichDrive"
```

Use your own folder's path instead if you chose a different drive folder. Nothing is deleted from your Immich server either way.
