using System;
using System.Windows.Input;
using ClaudeCouncilManager.Models;
using ClaudeCouncilManager.Services;
using Microsoft.Win32;

namespace ClaudeCouncilManager.ViewModels;

/// <summary>The «Impostazioni» tab: how sessions are opened and which PowerShell runs them.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly Action _changed;

    public SettingsViewModel(AppSettings model, Action changed)
    {
        Model = model;
        _changed = changed;
        BrowsePowerShellCommand = new RelayCommand(_ => BrowsePowerShell());
        AutoDetectPowerShellCommand = new RelayCommand(_ => PowerShellPath = "", _ => !string.IsNullOrWhiteSpace(PowerShellPath));
    }

    public AppSettings Model { get; }

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
                    ? "  (versione più recente installata, rilevata automaticamente)"
                    : "  (indicato manualmente)");
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
                return "Comando personalizzato: indica il programma da avviare.";
            if (!Model.CustomArguments.Contains("{pwshArgs}"))
                return "Comando personalizzato: gli argomenti devono contenere {pwsh} {pwshArgs}, che avviano la sessione di Claude in PowerShell.";
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
        var dlg = new OpenFileDialog { Title = "Scegli pwsh.exe (PowerShell 7)", Filter = "PowerShell|pwsh.exe|Programmi|*.exe" };
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
