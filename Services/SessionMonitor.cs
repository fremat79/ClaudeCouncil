using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ClaudeCouncilManager.Services;

/// <summary>A running claude.exe, the session name it was started with and the process that started it.</summary>
public sealed record RunningSession(string Name, int ProcessId, int ParentProcessId);

/// <summary>
/// Tells which council sessions are actually running, by reading the command lines of the
/// <c>claude.exe</c> processes and collecting their <c>--name</c>. Closing a terminal tab kills its
/// session, so this stays right however a session ended (unlike remembering what was launched).
/// The Claude desktop app is also <c>claude.exe</c> but never has <c>--name</c>, so it is ignored.
/// </summary>
public static partial class SessionMonitor
{
    // Shells a session runs in; only these are closed by «Termina» (never a terminal emulator itself).
    private static readonly string[] HostShells = { "pwsh", "powershell" };

    /// <summary>The running claude.exe processes started with --name; empty if WMI can't be queried.</summary>
    public static IReadOnlyList<RunningSession> RunningSessions()
    {
        var sessions = new List<RunningSession>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'claude.exe'");
            using var results = searcher.Get();
            foreach (var process in results)
            {
                using (process)
                {
                    if (process["CommandLine"] is string commandLine
                        && NameArgument().Match(commandLine) is { Success: true } m
                        && process["ProcessId"] is uint pid
                        && process["ParentProcessId"] is uint parent)
                        sessions.Add(new RunningSession(m.Groups["name"].Value, (int)pid, (int)parent));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            // WMI unavailable: report nothing running rather than blocking launches.
        }
        return sessions;
    }

    public static IReadOnlySet<string> RunningSessionNames()
        => RunningSessions().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ends a session: kills its claude.exe and everything it started (MCP servers, tool
    /// commands). The conversation is already saved by Claude Code, so it can be resumed.
    /// With <paramref name="closeTerminal"/> the PowerShell hosting it is closed too, with exit
    /// code 0 so Windows Terminal closes the tab (it keeps tabs whose process failed).
    /// </summary>
    public static void Terminate(RunningSession session, bool closeTerminal)
    {
        try
        {
            using var process = Process.GetProcessById(session.ProcessId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            throw new InvalidOperationException($"Impossibile terminare la sessione {session.Name}:\n\n{ex.Message}");
        }

        if (closeTerminal) CloseHostShell(session.ParentProcessId);
    }

    private static void CloseHostShell(int shellPid)
    {
        try
        {
            using var shell = Process.GetProcessById(shellPid);
            if (!HostShells.Contains(shell.ProcessName, StringComparer.OrdinalIgnoreCase)) return;
            // Sessions started by this version exit on their own once claude is gone (stop file
            // "close"); older ones sit at a -NoExit prompt and are ended here.
            if (shell.WaitForExit(2000)) return;
            if (!TerminateProcess(shell.SafeHandle, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (ArgumentException)
        {
            // Shell already gone.
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            throw new InvalidOperationException($"La sessione è terminata, ma non è stato possibile chiudere il suo terminale:\n\n{ex.Message}");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint exitCode);

    // --name X, --name=X, --name "X" or --name 'X' (council session names never contain spaces).
    [GeneratedRegex("""(?:^|\s)--name(?:\s+|=)(?:"(?<name>[^"]+)"|'(?<name>[^']+)'|(?<name>\S+))""")]
    private static partial Regex NameArgument();
}
