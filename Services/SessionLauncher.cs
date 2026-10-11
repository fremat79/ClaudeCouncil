using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ClaudeCouncilManager.Models;

namespace ClaudeCouncilManager.Services;

/// <summary>
/// Everything needed to start (or, with <see cref="Resume"/>, reopen) one interactive Claude Code
/// session.
/// </summary>
/// <param name="WorkingDirectory">The council's working folder: the session starts there.</param>
/// <param name="SessionId">Passed as <c>--session-id</c> for a new session, as <c>--resume</c> when resuming.</param>
/// <param name="SettingsFile">The expert's own <c>.claude\settings.json</c>, if any (<c>--settings</c>).</param>
/// <param name="AddDirs">The expert's folder (CLAUDE.md, skills) and the council's shared folder.</param>
/// <param name="StopFile">Created by the app before it terminates the session: tells the script the
/// exit was requested, so it closes quietly instead of keeping the error on screen.</param>
public sealed record LaunchRequest(
    string CouncilName,
    string ExpertName,
    string TabTitle,
    string? TabColor,
    string WorkingDirectory,
    string SessionName,
    Guid SessionId,
    bool Resume,
    string? Model,
    string? Effort,
    string? SettingsFile,
    IReadOnlyList<string> AddDirs,
    string? AppendSystemPromptFile,
    string? InitialPrompt,
    string StopFile);

/// <summary>
/// Starts <c>claude</c> through PowerShell 7 in the host chosen in the settings: a Windows Terminal
/// tab, a PowerShell window, or a custom program. The claude command line is handed to PowerShell
/// as <c>-EncodedCommand</c>: wt.exe treats <c>;</c> as its own command separator and re-quotes
/// arguments lossily, so free-text prompts would not survive a plain command line.
/// </summary>
public static partial class SessionLauncher
{
    // CreateProcess caps the whole command line at 32767 chars; leave room for the rest.
    private const int MaxEncodedLength = 30000;

    /// <summary>Placeholders understood in <see cref="AppSettings.CustomArguments"/>.</summary>
    public const string CustomPlaceholders = "{pwsh} {pwshArgs} {workdir} {title} {council} {expert} {color}";

    public static void Launch(LaunchRequest r, AppSettings settings)
    {
        var pwsh = PowerShellLocator.Resolve(settings.PowerShellPath);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildScript(r)));
        if (encoded.Length > MaxEncodedLength)
            throw new InvalidOperationException(
                "Il prompt iniziale è troppo lungo per la riga di comando. Accorcialo, oppure metti il testo "
                + "in un file nella cartella condivisa e nel prompt indica di leggerlo.");

        var startDir = Path.TrimEndingDirectorySeparator(r.WorkingDirectory);
        // -NoExit: the script itself decides when to close (see BuildScript); otherwise the prompt stays.
        var pwshArgs = new[] { "-NoLogo", "-NoExit", "-EncodedCommand", encoded };

        switch (settings.SessionHost)
        {
            case SessionHost.WindowsTerminal:
                StartWindowsTerminal(r, startDir, pwsh.Path, pwshArgs);
                break;

            case SessionHost.PowerShellWindow:
                var ps = new ProcessStartInfo { FileName = pwsh.Path, UseShellExecute = true, WorkingDirectory = startDir };
                foreach (var a in pwshArgs) ps.ArgumentList.Add(a);
                Process.Start(ps);
                break;

            case SessionHost.Custom:
                var custom = new ProcessStartInfo
                {
                    FileName = settings.CustomProgram.Trim(),
                    Arguments = ExpandPlaceholders(settings.CustomArguments, r, startDir, pwsh.Path, string.Join(' ', pwshArgs)),
                    UseShellExecute = true,
                    WorkingDirectory = startDir,
                };
                try
                {
                    Process.Start(custom);
                }
                catch (Win32Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Impossibile avviare il programma personalizzato «{settings.CustomProgram}»:\n\n{ex.Message}");
                }
                break;
        }
    }

    /// <summary>A tab in the council's named Windows Terminal window (one window per council).</summary>
    private static void StartWindowsTerminal(LaunchRequest r, string startDir, string pwshPath, string[] pwshArgs)
    {
        // UseShellExecute lets the wt.exe app-execution alias resolve.
        var wt = new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = true };
        wt.ArgumentList.Add("-w");
        wt.ArgumentList.Add("ClaudeCouncil-" + r.CouncilName);
        wt.ArgumentList.Add("new-tab");
        wt.ArgumentList.Add("--title");
        wt.ArgumentList.Add(r.TabTitle);
        if (Naming.IsHexColor(r.TabColor))
        {
            wt.ArgumentList.Add("--tabColor");
            wt.ArgumentList.Add(r.TabColor);
        }
        wt.ArgumentList.Add("--suppressApplicationTitle");
        wt.ArgumentList.Add("-d");
        wt.ArgumentList.Add(startDir);
        wt.ArgumentList.Add(pwshPath);
        foreach (var a in pwshArgs) wt.ArgumentList.Add(a);
        try
        {
            Process.Start(wt);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException(
                "Windows Terminal non trovato. Installalo oppure, nelle Impostazioni, scegli di aprire le "
                + "sessioni in finestre PowerShell.");
        }
    }

    /// <summary>
    /// Fills a custom arguments template. Values are inserted as they are: put quotes around
    /// placeholders whose value may contain spaces, e.g. <c>--cwd "{workdir}"</c>.
    /// </summary>
    public static string ExpandPlaceholders(string template, LaunchRequest r, string workDir, string pwshPath, string pwshArgs)
        => template
            .Replace("{pwshArgs}", pwshArgs)
            .Replace("{pwsh}", pwshPath)
            .Replace("{workdir}", workDir)
            .Replace("{title}", r.TabTitle)
            .Replace("{council}", r.CouncilName)
            .Replace("{expert}", r.ExpertName)
            .Replace("{color}", r.TabColor ?? "");

    /// <summary>
    /// The PowerShell script run for the session (pwsh runs it with -NoExit). When claude ends:
    /// stopped by «Termina», the stop file says "close" (exit 0: Windows Terminal closes the tab) or
    /// "keep" (the shell stays at its prompt with a note); a normal exit (/exit) exits 0 and closes
    /// the tab or window; a failure leaves the shell at its prompt so the error can be read.
    /// Public for testing the quoting.
    /// </summary>
    public static string BuildScript(LaunchRequest r)
    {
        var sb = new StringBuilder();
        // Only visible outside Windows Terminal; in wt the tab title is fixed.
        sb.Append("$Host.UI.RawUI.WindowTitle = ").Append(PsLiteral(r.TabTitle)).Append('\n');
        sb.Append("$stopFile = ").Append(PsLiteral(r.StopFile)).Append('\n');
        sb.Append("if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile -Force }\n");
        // The expert's CLAUDE.md sits in an --add-dir folder: Claude Code reads it only with this set.
        sb.Append("$env:CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD = '1'\n");

        sb.Append("claude --name ").Append(PsLiteral(r.SessionName));
        sb.Append(r.Resume ? " --resume " : " --session-id ").Append(PsLiteral(r.SessionId.ToString()));
        if (!string.IsNullOrWhiteSpace(r.Model)) sb.Append(" --model ").Append(PsLiteral(r.Model.Trim()));
        if (!string.IsNullOrWhiteSpace(r.Effort)) sb.Append(" --effort ").Append(PsLiteral(r.Effort.Trim()));
        if (r.SettingsFile is not null) sb.Append(" --settings ").Append(PsLiteral(r.SettingsFile));
        foreach (var dir in r.AddDirs)
            sb.Append(" --add-dir ").Append(PsLiteral(Path.TrimEndingDirectorySeparator(dir)));
        if (r.AppendSystemPromptFile is not null)
            sb.Append(" --append-system-prompt-file ").Append(PsLiteral(r.AppendSystemPromptFile));
        // --add-dir takes several values: without "--" the prompt would be read as one more folder.
        if (!string.IsNullOrWhiteSpace(r.InitialPrompt)) sb.Append(" -- ").Append(PsLiteral(r.InitialPrompt.Trim()));
        sb.Append('\n');

        sb.Append("$code = $LASTEXITCODE\n");
        sb.Append("if (Test-Path -LiteralPath $stopFile) {\n");
        sb.Append("    $mode = (Get-Content -LiteralPath $stopFile -Raw).Trim()\n");
        sb.Append("    Remove-Item -LiteralPath $stopFile -Force\n");
        sb.Append("    if ($mode -eq 'close') { exit 0 }\n");
        sb.Append("    Write-Host ''\n");
        sb.Append("    Write-Host 'Sessione terminata da Claude Council Manager.' -ForegroundColor Yellow\n");
        sb.Append("} elseif ($code -eq 0) {\n");
        sb.Append("    exit 0\n");
        sb.Append("} else {\n");
        sb.Append("    Write-Host ''\n");
        sb.Append("    Write-Host \"claude si è chiuso con il codice $code.\" -ForegroundColor Yellow\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>
    /// A PowerShell single-quoted literal. PowerShell also treats the typographic quotes
    /// U+2018..U+201B as single quotes (common in Italian text: l’API), so those are doubled too.
    /// </summary>
    private static string PsLiteral(string value) => "'" + PsSingleQuote().Replace(value, "$1$1") + "'";

    [GeneratedRegex("(['‘’‚‛])")]
    private static partial Regex PsSingleQuote();
}
