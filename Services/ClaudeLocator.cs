using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeCouncilManager.Services;

/// <summary>Where Claude Code is and whether councils can be launched with it (<see cref="Problem"/> null).</summary>
public sealed record ClaudeStatus(string? Path, Version? Version, string? Problem)
{
    public bool IsUsable => Problem is null && Path is not null;

    public string Description => Path is null ? Problem ?? "" : $"Claude Code {Version} — {Path}";
}

/// <summary>
/// Finds the Claude Code CLI and checks its version. Councils can be configured without it, but
/// launching needs it: sessions run that exact executable (SessionLauncher) and are recognized by
/// their claude.exe process (SessionMonitor), so only the native install (claude.exe) is used.
/// </summary>
public static partial class ClaudeLocator
{
    /// <summary>First version with cross-session messaging on native Windows (how council members talk).</summary>
    public static readonly Version Minimum = new(2, 1, 234);

    public static ClaudeStatus Check(string? configuredPath)
    {
        string path;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            path = configuredPath.Trim();
            if (!File.Exists(path))
                return new ClaudeStatus(null, null, "The Claude Code path set in Settings doesn't exist:\n" + path);
        }
        else if (FindNativeInstall() is { } found)
        {
            path = found;
        }
        else
        {
            return new ClaudeStatus(null, null, IsOnPath("claude.cmd")
                ? "Only the npm version of Claude Code is installed; councils need the native install (claude.exe). "
                  + "Install it with 'claude install', then click Recheck."
                : "Claude Code is not installed (claude.exe not found). Install it from https://code.claude.com "
                  + "and click Recheck, or set the path of claude.exe in Settings.");
        }

        var (version, output) = ReadVersion(path);
        if (version is null)
            return new ClaudeStatus(path, null, $"Can't read the Claude Code version ({path}):\n{output}");
        if (version < Minimum)
            return new ClaudeStatus(path, version,
                $"Claude Code {version} is too old: council members need {Minimum} or later "
                + "to message each other. Update it with 'claude update', then click Recheck.");
        return new ClaudeStatus(path, version, null);
    }

    /// <summary>claude.exe on PATH, else where the native installer puts it (%USERPROFILE%\.local\bin).</summary>
    private static string? FindNativeInstall()
        => FindOnPath("claude.exe")
           ?? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe") }
               .FirstOrDefault(File.Exists);

    private static bool IsOnPath(string fileName) => FindOnPath(fileName) is not null;

    private static string? FindOnPath(string fileName)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry – skip it.
            }
        }
        return null;
    }

    /// <summary>Runs <c>claude --version</c> ("2.1.296 (Claude Code)").</summary>
    private static (Version? Version, string Output) ReadVersion(string path)
    {
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                return (null, "no answer to 'claude --version' within 15 seconds");
            }
            var output = stdout.Result.Trim();
            var m = VersionPattern().Match(output);
            return m.Success
                ? (new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), output)
                : (null, output.Length > 0 ? output : process.StandardError.ReadToEnd().Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (null, ex.Message);
        }
    }

    [GeneratedRegex(@"^\s*(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
