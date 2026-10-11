using System;
using System.IO;

namespace ClaudeCouncilManager.Services;

/// <summary>
/// Where the app keeps its own data under <c>%LOCALAPPDATA%\ClaudeCouncilManager</c>: the SQLite
/// database and the per-council runtime folders (generated protocols + shared exchange folder).
/// </summary>
public static class AppPaths
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly string AppDataDir = Path.Combine(LocalAppData, "ClaudeCouncilManager");

    public static readonly string DatabasePath = Path.Combine(AppDataDir, "claudecouncil.db");

    private static readonly string RuntimeRoot = Path.Combine(AppDataDir, "runtime");

    // Data folder used before the app was renamed from ClaudeCouncil.
    private static readonly string LegacyAppDataDir = Path.Combine(LocalAppData, "ClaudeCouncil");

    /// <summary>
    /// First start after the rename: copies the database and runtime folders from the old data
    /// folder. A copy, not a move: sessions still open from the old version keep using the old
    /// protocol/shared paths, and the old folder stays as a backup the user can delete.
    /// </summary>
    public static void MigrateLegacyDataFolder()
    {
        if (Directory.Exists(AppDataDir) || !File.Exists(Path.Combine(LegacyAppDataDir, "claudecouncil.db")))
            return;

        var staging = AppDataDir + ".migrating";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        CopyDirectory(LegacyAppDataDir, staging);
        // Only a complete copy becomes the data folder; a failed one is retried on the next start.
        Directory.Move(staging, AppDataDir);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("config.json", StringComparison.OrdinalIgnoreCase)) continue; // pre-SQLite leftover
            File.Copy(file, Path.Combine(target, name));
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    /// <summary>Folder holding a council's generated protocols; created on demand.</summary>
    public static string CouncilRuntimeDir(string councilName)
    {
        var dir = Path.Combine(RuntimeRoot, councilName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Signal file the app creates before terminating a member's session (see SessionLauncher).</summary>
    public static string StopFile(string councilName, string expertName)
        => Path.Combine(CouncilRuntimeDir(councilName), expertName + ".stop");

    /// <summary>Folder every member of a council gets via <c>--add-dir</c> to exchange files.</summary>
    public static string CouncilSharedDir(string councilName)
    {
        var dir = Path.Combine(CouncilRuntimeDir(councilName), "shared");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
