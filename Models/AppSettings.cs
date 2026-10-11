namespace ClaudeCouncilManager.Models;

/// <summary>Where a council member's session is opened.</summary>
public enum SessionHost
{
    /// <summary>A tab in the council's Windows Terminal window (one window per council).</summary>
    WindowsTerminal = 0,

    /// <summary>A PowerShell console window per member.</summary>
    PowerShellWindow = 1,

    /// <summary>A user-defined program/arguments template (another terminal emulator, …).</summary>
    Custom = 2,
}

/// <summary>App-wide settings: a single row (<see cref="Id"/> = 1) in the database.</summary>
public sealed class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public SessionHost SessionHost { get; set; } = SessionHost.WindowsTerminal;

    /// <summary>Explicit pwsh.exe to use; empty means the newest PowerShell 7 found on the machine.</summary>
    public string? PowerShellPath { get; set; }

    /// <summary>Explicit claude.exe to use; empty means the one found by ClaudeLocator.</summary>
    public string? ClaudePath { get; set; }

    /// <summary>Program started for <see cref="SessionHost.Custom"/>.</summary>
    public string CustomProgram { get; set; } = "";

    /// <summary>Its arguments, with placeholders such as {pwsh} and {pwshArgs} (see SessionLauncher).</summary>
    public string CustomArguments { get; set; } = "";

    /// <summary>
    /// Stop also closes the terminal tab/window hosting the session. Remembered from the
    /// last choice in the stop dialog.
    /// </summary>
    public bool CloseTerminalOnTerminate { get; set; } = true;
}
