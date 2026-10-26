namespace Qwader.Application.Settings;

public interface ISystemBackupService
{
    /// <summary>File name for the download, e.g. «qwader-backup-2026-10-26-1430.zip» (store-local time).</summary>
    string FileName();

    /// <summary>
    /// Writes a ZIP of the whole store: every table as JSON (passwords and sign-in tokens left out),
    /// readable Excel sheets of the main lists, and the uploaded files. Admin only.
    /// </summary>
    Task WriteAsync(Stream output, CancellationToken ct = default);
}
