using ImmichDrive.Services;
using WinUIAppKit.Localization;

namespace ImmichDrive.Classes;

/// <summary>
/// The one-line drive status shared by the tray tooltip, the tray menu's status line and the flyout,
/// so the three always say the same thing, in the current interface language.
/// </summary>
internal static class DriveStatusText
{
    /// <summary>
    /// A short summary of the drive's state, such as "Up to date" or "Syncing 40 of about 900…".
    /// </summary>
    /// <param name="dm">The drive whose state to describe.</param>
    /// <param name="includeChecking">
    /// True to report a connection re-test in progress ("Checking the connection…") ahead of the
    /// state. The flyout shows it live; the tray menu is a snapshot and leaves it out.
    /// </param>
    public static string Summary(DriveManager dm, bool includeChecking = false)
    {
        if (includeChecking && dm.IsCheckingConnection)
        {
            return UiText.Get("status.checking");
        }

        var (done, total) = dm.Progress;
        return dm.Status switch
        {
            DriveStatus.Online when dm.IsPopulating && total > done => UiText.Format("status.syncing",
                ("done", Count(done)),
                ("total", Count(total))),
            DriveStatus.Online when dm.IsPopulating => UiText.Get("status.finishing"),
            DriveStatus.Online when dm.SyncIssue != null => UiText.Get("status.sync-issue"),
            DriveStatus.Online => UiText.Get("status.up-to-date"),
            DriveStatus.Connecting => UiText.Get("status.connecting"),
            DriveStatus.Offline => UiText.Get("status.offline"),
            DriveStatus.Error => UiText.Get("status.problem"),
            _ => UiText.Get("status.disconnected"),
        };
    }

    /// <summary>
    /// The tray tooltip: the product name and the summary, or the error's own detail when there is
    /// one, since the tooltip is the only place a passive error is spelled out without opening anything.
    /// </summary>
    public static string Tooltip(DriveManager dm)
    {
        string status = dm.Status == DriveStatus.Error && dm.StatusDetail is { } detail
            ? detail.Text
            : Summary(dm);
        return UiText.Format("tray.tooltip", ("status", status));
    }

    /// <summary>
    /// "Downloading 3 of 12 pinned photos…" while pinned photos are downloading, or null when nothing
    /// is, so the caller can hide its progress row.
    /// </summary>
    public static string? PinProgress(DriveManager dm)
    {
        var (done, total, active) = dm.PinProgress;
        if (dm.Status != DriveStatus.Online || !active || total <= 0)
        {
            return null;
        }

        return UiText.Format("common.pins-progress", ("done", Count(done)), ("total", Count(total)));
    }

    /// <summary>
    /// A count for status text, with the device region's digit grouping ("1,234"). Numbers and times
    /// follow the region, not the interface language (LOC-4).
    /// </summary>
    public static string Count(int value) => value.ToString("N0");

    /// <summary>A local time of day for status text, in the region's short format ("12:04 p.m.").</summary>
    public static string ShortTime(DateTimeOffset utc) => utc.ToLocalTime().ToString("t");

    /// <summary>A local date and time for status text, in the region's short format.</summary>
    public static string ShortDateTime(DateTimeOffset utc) => utc.ToLocalTime().ToString("g");
}
