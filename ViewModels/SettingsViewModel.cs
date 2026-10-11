using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ClaudeCouncilManager.Models;
using ClaudeCouncilManager.Services;
using Microsoft.Win32;

namespace ClaudeCouncilManager.ViewModels;

/// <summary>
/// The Settings tab: how sessions are opened, which PowerShell runs them, and whether Claude
/// Code is installed (without it councils can be configured but not launched).
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly Action _changed;
    private ClaudeStatus? _claude; // null while the first check runs
    private int _claudeCheck;

    public SettingsViewModel(AppSettings model, Action changed)
    {
        Model = model;
        _changed = changed;
        BrowsePowerShellCommand = new RelayCommand(_ => BrowsePowerShell());
        AutoDetectPowerShellCommand = new RelayCommand(_ => PowerShellPath = "", _ => !string.IsNullOrWhiteSpace(PowerShellPath));
        RecheckClaudeCommand = new RelayCommand(async _ => await RecheckClaudeAsync());
        BrowseClaudeCommand = new RelayCommand(_ => BrowseClaude());
        AutoDetectClaudeCommand = new RelayCommand(_ => ClaudePath = "", _ => !string.IsNullOrWhiteSpace(ClaudePath));
    }

    public AppSettings Model { get; }

    // ---- Claude Code -------------------------------------------------------------------------

    /// <summary>Councils can be launched only when this is true (also false while checking).</summary>
    public bool IsClaudeUsable => _claude?.IsUsable == true;

    /// <summary>Why councils can't be launched, or null (also null while the check is running).</summary>
    public string? ClaudeProblem => _claude?.Problem;

    public string ClaudeInfo => _claude switch
    {
        null => "Checking Claude Code…",
        { IsUsable: true } c => c.Description + (string.IsNullOrWhiteSpace(Model.ClaudePath)
            ? "  (detected automatically)"
            : "  (set manually)"),
        { } c => c.Problem!,
    };

    public string ClaudeRequirement =>
        $"Requires Claude Code {ClaudeLocator.Minimum} or later, native install (claude.exe). "
        + "Without it, experts and councils can be created and edited but not started.";

    /// <summary>The verified claude.exe, or null.</summary>
    public string? ClaudeExecutable => IsClaudeUsable ? _claude!.Path : null;

    /// <summary>Explicit claude.exe; empty = found automatically.</summary>
    public string ClaudePath
    {
        get => Model.ClaudePath ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (v == Model.ClaudePath) return;
            Model.ClaudePath = v;
            OnPropertyChanged();
            _changed();
            _ = RecheckClaudeAsync();
        }
    }

    public ICommand RecheckClaudeCommand { get; }

    public ICommand BrowseClaudeCommand { get; }

    public ICommand AutoDetectClaudeCommand { get; }

    /// <summary>Looks for claude.exe and reads its version (off the UI thread); the last check wins.</summary>
    public async Task RecheckClaudeAsync()
    {
        var check = ++_claudeCheck;
        var path = Model.ClaudePath;
        var status = await Task.Run(() => ClaudeLocator.Check(path));
        if (check != _claudeCheck) return;
        _claude = status;
        foreach (var p in new[] { nameof(IsClaudeUsable), nameof(ClaudeProblem), nameof(ClaudeInfo), nameof(ClaudeExecutable) })
            OnPropertyChanged(p);
        CommandManager.InvalidateRequerySuggested(); // launch buttons depend on it
    }

    private void BrowseClaude()
    {
        var dlg = new OpenFileDialog { Title = "Choose claude.exe (Claude Code)", Filter = "Claude Code|claude.exe|Programs|*.exe" };
        if (dlg.ShowDialog() == true) ClaudePath = dlg.FileName;
    }

    // ---- Session host and PowerShell ---------------------------------------------------------

    // One bool per option so each RadioButton can bind two-way without a converter.
    public bool UseWindowsTerminal
    {
        get => Model.SessionHost == SessionHost.WindowsTerminal;
        set { if (value) SetHost(SessionHost.WindowsTerminal); }
    }

    public bool UsePowerShellWindow
    {
        get => Model.SessionHost == SessionHost.PowerShellWindow;
        set { if (value) SetHost(SessionHost.PowerShellWindow); }
    }

    public bool UseCustom
    {
        get => Model.SessionHost == SessionHost.Custom;
        set { if (value) SetHost(SessionHost.Custom); }
    }

    /// <summary>Explicit pwsh.exe; empty = newest PowerShell 7 found.</summary>
    public string PowerShellPath
    {
        get => Model.PowerShellPath ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (v == Model.PowerShellPath) return;
            Model.PowerShellPath = v;
            NotifyAll();
        }
    }

    /// <summary>Which PowerShell sessions will run with, or why none can be used.</summary>
    public string PowerShellInfo
    {
        get
        {
            try
            {
                var pwsh = PowerShellLocator.Resolve(Model.PowerShellPath);
                return pwsh.Description + (string.IsNullOrWhiteSpace(Model.PowerShellPath)
                    ? "  (newest installed version, detected automatically)"
                    : "  (set manually)");
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }
    }

    public string CustomProgram
    {
        get => Model.CustomProgram;
        set
        {
            if (value == Model.CustomProgram) return;
            Model.CustomProgram = value;
            NotifyAll();
        }
    }

    public string CustomArguments
    {
        get => Model.CustomArguments;
        set
        {
            if (value == Model.CustomArguments) return;
            Model.CustomArguments = value;
            NotifyAll();
        }
    }

    public string CustomPlaceholders => SessionLauncher.CustomPlaceholders;

    /// <summary>Why sessions can't be launched with these settings, or null.</summary>
    public string? Error
    {
        get
        {
            try
            {
                PowerShellLocator.Resolve(Model.PowerShellPath);
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
            if (Model.SessionHost != SessionHost.Custom) return null;
            if (string.IsNullOrWhiteSpace(Model.CustomProgram))
                return "Custom command: set the program to start.";
            if (!Model.CustomArguments.Contains("{pwshArgs}"))
                return "Custom command: the arguments must contain {pwsh} {pwshArgs}, which start the Claude session in PowerShell.";
            return null;
        }
    }

    public ICommand BrowsePowerShellCommand { get; }

    public ICommand AutoDetectPowerShellCommand { get; }

    private void SetHost(SessionHost host)
    {
        if (Model.SessionHost == host) return;
        Model.SessionHost = host;
        // A first-time custom template: an example to adapt rather than an empty box.
        if (host == SessionHost.Custom && string.IsNullOrWhiteSpace(Model.CustomProgram) && string.IsNullOrWhiteSpace(Model.CustomArguments))
        {
            Model.CustomProgram = "wezterm";
            Model.CustomArguments = "start --cwd \"{workdir}\" -- \"{pwsh}\" {pwshArgs}";
        }
        NotifyAll();
    }

    private void BrowsePowerShell()
    {
        var dlg = new OpenFileDialog { Title = "Choose pwsh.exe (PowerShell 7)", Filter = "PowerShell|pwsh.exe|Programs|*.exe" };
        if (dlg.ShowDialog() == true) PowerShellPath = dlg.FileName;
    }

    private void NotifyAll()
    {
        foreach (var p in new[]
                 {
                     nameof(UseWindowsTerminal), nameof(UsePowerShellWindow), nameof(UseCustom), nameof(PowerShellPath),
                     nameof(PowerShellInfo), nameof(CustomProgram), nameof(CustomArguments), nameof(Error),
                 })
            OnPropertyChanged(p);
        _changed();
    }
}
