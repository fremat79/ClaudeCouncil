using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ClaudeCouncilManager.Services;

/// <summary>A pwsh.exe and its version (null when it can't be read, e.g. a Store app alias).</summary>
public sealed record PowerShellInstall(string Path, Version? Version)
{
    public string Description => Version is null ? Path : $"PowerShell {Version} — {Path}";
}

/// <summary>
/// Finds PowerShell 7 to run sessions with. Sessions always use PowerShell 7+ (never Windows
/// PowerShell 5.1): from 7.3 on, native arguments with quotes are passed through correctly.
/// </summary>
public static class PowerShellLocator
{
    public static readonly Version Minimum = new(7, 3);

    /// <summary>The newest stable install found (a preview only if nothing else is installed).</summary>
    public static PowerShellInstall? FindLatest()
    {
        var found = Candidates()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Describe)
            .OfType<PowerShellInstall>()
            .ToList();
        return found
            .OrderBy(p => p.Version is null)                                          // known versions first
            .ThenBy(p => p.Path.Contains("preview", StringComparison.OrdinalIgnoreCase)) // stable before preview
            .ThenByDescending(p => p.Version)
            .FirstOrDefault();
    }

    public static PowerShellInstall? Describe(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileMajorPart > 0
                ? new PowerShellInstall(path, new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart))
                : new PowerShellInstall(path, null);
        }
        catch
        {
            return new PowerShellInstall(path, null); // e.g. an app execution alias
        }
    }

    /// <summary>The PowerShell to launch with; throws with a user-facing message when there is none usable.</summary>
    public static PowerShellInstall Resolve(string? configuredPath)
    {
        PowerShellInstall? pwsh;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            pwsh = Describe(configuredPath.Trim())
                   ?? throw new InvalidOperationException(
                       "Il PowerShell indicato nelle Impostazioni non esiste:\n\n" + configuredPath);
        }
        else
        {
            pwsh = FindLatest() ?? throw new InvalidOperationException(
                "PowerShell 7 non trovato. Installalo (winget install Microsoft.PowerShell) "
                + "oppure indica il percorso di pwsh.exe nelle Impostazioni.");
        }

        if (pwsh.Version is not null && pwsh.Version < Minimum)
            throw new InvalidOperationException(
                $"Serve PowerShell {Minimum} o successivo; quello configurato è {pwsh.Version}:\n\n{pwsh.Path}");
        return pwsh;
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            var dir = Path.Combine(root, "PowerShell");
            if (!Directory.Exists(dir)) continue;
            foreach (var version in Directory.EnumerateDirectories(dir))
                yield return Path.Combine(version, "pwsh.exe");
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "pwsh.exe");

        // Anything on PATH, including the Store install's app execution alias (version unknown).
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim(), "pwsh.exe");
            }
            catch (ArgumentException)
            {
                continue; // malformed PATH entry
            }
            yield return candidate;
        }
    }
}
