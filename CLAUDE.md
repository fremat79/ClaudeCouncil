# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this app is

**Claude Council Manager** (`ClaudeCouncilManager.csproj`, namespace `ClaudeCouncilManager`; formerly
*ClaudeCouncil*) is a WPF (.NET 10, Windows) app to configure and launch **councils** of
interactive Claude Code sessions that talk to each other. User-facing docs and onboarding:
`README.md`; sample experts: `Example\`.

- **Expert** = a folder on disk with a `CLAUDE.md`, optionally skills in
  `.claude\skills\<name>\SKILL.md` and permissions in `.claude\settings.json`. It is configuration
  only: sessions don't start there (see "Launch mechanics" for how it's loaded).
- **Coordinator** = a flag on exactly one expert (`Expert.IsCoordinator`; setting it on one
  clears it on the others via `MainViewModel.OnBecameCoordinator`).
- **Council** = a **working folder** (required, `Council.WorkingFolder`: every member's session
  starts there) + the experts the user picks (`Council.Members`, rows of `CouncilMember`). With two or more picked
  besides the coordinator, the coordinator joins automatically (derived in
  `CouncilViewModel.Members`, never stored; its checkbox is ticked and locked). With a single
  expert there is no coordinator and no protocol: it's a plain named session. "Start" opens one
  Windows Terminal tab per member with new conversations, "Resume" reopens the last ones. "New council" stays disabled until the coordinator and at
  least one other expert exist (`MainViewModel.NewCouncilBlockedReason`, shown under the header).

- **Favorites** = `IsFavorite` on experts and councils (heart toggle on each card). Personal, so
  DB-only: never written to expert.json — `ExpertViewModel.IsFavorite` reports through
  `changedLocally`, not `edited`. Both tabs list through `MainViewModel.FavoritesFirst` views
  (favorites first, then name; live sorting/filtering on `IsFavorite` only, so cards don't jump
  while a name is typed) with a "Favorites only" filter.

All UI text, generated protocols, templates, samples and docs are in English. A heart marks favorites because ★ already marks the coordinator.

## Commands

```powershell
dotnet build        # Debug build
dotnet run          # build + launch (applies pending EF migrations on startup)
dotnet tool restore # once: restores the local dotnet-ef (dotnet-tools.json)
dotnet ef migrations add <Name> --output-dir Data/Migrations   # after changing the model

# Installer (same pattern as ..\ClaudeCodeExplorer): single-file, self-contained, win-x64, R2R
dotnet publish ClaudeCouncilManager.csproj -c Release -p:PublishProfile=FolderProfile
& "C:\Program Files (x86)\Inno Setup 5\ISCC.exe" Installer.iss   # -> dist\ClaudeCouncilManager-Setup-<Version>.exe
```

`Installer.iss`: per-user install (`PrivilegesRequired=none`; run elevated, Inno Setup 5 installs
for all users instead), its own AppId (distinct from ClaudeCodeExplorer's), version read from the
published exe (`<Version>` in the .csproj). It kills a running `ClaudeCouncilManager.exe` before
installing; Claude sessions are left alone. Uninstall asks about deleting
`%LOCALAPPDATA%\ClaudeCouncilManager` (plus the legacy `ClaudeCouncil` copy) with **No** as
default, and never asks when silent, so `/VERYSILENT /SUPPRESSMSGBOXES` can't wipe the database.

There is no test project and no linter configured — build is the only gate.

## Architecture

MVVM written by hand (no framework), same conventions as the sibling `..\ClaudeCodeExplorer`:
`ObservableObject` / `RelayCommand`, stateless `static` services under `Services/`, one window
bound to `MainViewModel`, user actions wrapped in `Run(...)` / `RunAsync(...)` which show
exceptions as a `MessageBox`. `ThemeManager` swaps `Themes/Light.xaml` / `Dark.xaml`; all XAML
colors are `DynamicResource` brush keys from those dictionaries.

**Persistence:** SQLite via EF Core (`Data/CouncilDbContext`, database at
`%LOCALAPPDATA%\ClaudeCouncilManager\claudecouncil.db`, path in `Services/AppPaths`; on first start
`AppPaths.MigrateLegacyDataFolder` *copies* the pre-rename `%LOCALAPPDATA%\ClaudeCouncil` there, so
sessions still running from the old version keep their paths). Tables: `Experts`,
`Councils`, `CouncilMembers` (composite key, cascade from both sides, `Order` keeps pick order),
`CouncilSessions` (last session id per council member, for "Resume"; cleared when the working
folder changes because Claude Code ties conversations to their start folder).
`MainViewModel` owns one context for the app's lifetime and `Migrate()`s it at startup; view
models edit the tracked entities in place and call `MainViewModel.Changed()`, which revalidates
and schedules a debounced `SaveNow()` (also run on window close). Adding/removing goes through
`_db.Experts` / `_db.Councils`; a member removed from `Council.Members` is an orphan and EF
deletes its row. `ExpertViewModel` status (CLAUDE.md present, skills) comes from
`ExpertInspector` and is never persisted.

**expert.json (sharing experts):** each expert folder carries its settings in `expert.json`
(`ExpertManifest`: name, description, color, model, effort, isCoordinator), so a set of expert
folders can live in a git repo and be imported by a colleague. The DB stays the working copy:
- `SaveNow` rewrites expert.json only for experts edited in the app since the last save
  (`_dirtyManifests`, fed by `ExpertViewModel`'s `edited` callback), and only if the content
  differs — a manifest pulled from git is never overwritten by an expert nobody touched;
- at startup and on "Refresh" `RefreshExperts(adoptManifests: true)` adopts expert.json values
  into the DB (`ExpertViewModel.ApplyManifest`), skipping experts with unsaved edits; the name is
  adopted only if valid and free, the coordinator flag never (it's honoured only at import);
- "Import experts…" → `ExpertImporter.Scan` (the chosen folder + its direct non-dot subfolders
  with CLAUDE.md or expert.json) → `Views/ImportWindow` preview (`ImportViewModel`) → registered
  via `CreateExpert`/`Register`. Imports never write into the source folders; the first imported
  coordinator is honoured only if the app has none.

**How sessions talk:** nothing in this app relays messages. Claude Code's native cross-session
messaging (`ListAgents` / `SendMessage`, `/peers`; https://code.claude.com/docs/en/cross-session-messaging)
addresses a session by its `--name`. The app only sets things up:
- session name = `Naming.SessionName(council, expert)` → `COUNCIL.EXPERT`, so two councils can
  share an expert without name collisions; names are restricted to `[A-Za-z0-9_-]`;
- for councils with >1 member, `ProtocolBuilder` renders `Templates/CouncilProtocol.md` +
  `CoordinatorSection.md` / `MemberSection.md` into
  `runtime\<COUNCIL>\<EXPERT>.protocol.md`, passed with `--append-system-prompt-file`;
- every member gets `runtime\<COUNCIL>\shared` via `--add-dir` for file exchange;
- the coordinator (or the only expert) is launched last (after `CoordinatorDelay`) and alone
  receives the council's initial prompt, only when "Start" starts the council from scratch.

**Sessions and resume (`OpenCouncilAsync`):** every new session gets an id chosen by the app
(`--session-id`, stored with `Council.SetSession`); Claude Code saves it as
`~\.claude\projects\<encoded working folder>\<id>.jsonl` (`SessionHistory.Exists`). "Resume"
relaunches each closed member with `--resume <id>` from the same working folder; a member whose
transcript is gone starts a new conversation. Clicking a closed member's chip resumes it if it can.
Neither ever resends the initial prompt.

**Which sessions are running (`SessionMonitor`):** read from the live `claude.exe` processes
(WMI `Win32_Process.CommandLine`, the `--name` argument), never from what the app remembers
launching — a tab closed without `/exit` kills its session. Polled every 5 s off the UI thread for
the "● running" chips, and re-read fresh before every launch: "Start"/"Resume" open only the
missing members and a running session is never launched twice. "Start" is disabled while any of the
council's sessions runs (`HasRunningSessions`: stop them with ■ first), "Resume" only while all of
them run (`AllSessionsRunning`); the poll calls `CommandManager.InvalidateRequerySuggested()` when
the running set changes, since it happens without user input. The Claude desktop app is also `claude.exe` but has no `--name`, so it's ignored.

Templates are `EmbeddedResource`s with logical name `Templates.<file>` (see the .csproj), read
through `Services/Templates`.

**Claude Code availability (`ClaudeLocator`, `SettingsViewModel.RecheckClaudeAsync`):** councils
can always be configured, but launching needs a native `claude.exe` ≥ 2.1.234 (cross-session
messaging on native Windows). It's looked up on PATH, then `%USERPROFILE%\.local\bin`, or taken from
`AppSettings.ClaudePath`, and its version read from `claude --version` off the UI thread. An
npm-only install (`claude.cmd`, process `node.exe`) is reported as unsupported because
SessionMonitor recognizes sessions by their claude.exe process. Checked at startup, on "Recheck"
or a path change, and again right before every launch (`IsClaudeReadyAsync`). Launch commands are
disabled until a check succeeds, and the Councils tab shows a banner while it doesn't. The session
script runs that exact path (`& '<claude.exe>' …`), never whatever `claude` resolves to.

**Launch mechanics (`SessionLauncher`):** always PowerShell 7 (`PowerShellLocator`: newest stable
pwsh ≥ 7.3 found, or the path set in the settings; 5.1 is not supported), started as
`pwsh -NoLogo -EncodedCommand <base64>` in the host chosen in the Settings tab
(`AppSettings.SessionHost`, single DB row): a tab via `wt.exe -w ClaudeCouncil-<COUNCIL> new-tab …`,
a PowerShell window, or a custom program/arguments template (`ExpandPlaceholders`). No silent
fallback: a missing wt/pwsh is reported with a hint to change the settings. The
claude command line goes through `-EncodedCommand` because wt splits its own command line on `;`
and re-quotes arguments lossily. Inside the script every argument is a PowerShell single-quoted
literal (`PsLiteral`, which also doubles the typographic quotes U+2018–U+201B that PowerShell
treats as `'`); pwsh ≥ 7.3 passes embedded `"` to native programs correctly.

**Ending sessions:** pwsh runs the script with `-NoExit`, and the script decides: after a normal
claude exit it runs `exit 0` (Windows Terminal's default `closeOnExit` closes tabs that exit with 0,
it keeps failed ones); after a failure it stays at the prompt so the error is readable. "Stop" (■
on the card, or a member's right-click menu → `TerminateAsync`) opens `Views/TerminateWindow`, whose
"Also close the terminal windows/tabs" box is remembered in
`AppSettings.CloseTerminalOnTerminate`. It writes `runtime\<COUNCIL>\<EXPERT>.stop` containing
`close` or `keep`, then kills the claude.exe tree found by `SessionMonitor.RunningSessions`
(pid, parent pid, `--name`). The script reads the stop file: `close` → `exit 0`, `keep` → stays at
the prompt with a note. With `close`, `SessionMonitor.Terminate` also ends the parent shell (pwsh or
powershell only) if it's still alive after 2 s, via `TerminateProcess(…, 0)`: exit code 0 is what
makes Windows Terminal close the tab, and it covers sessions started by older versions (a plain
`-NoExit` prompt). The script deletes a leftover stop file on start. All three cases were verified
with real Windows Terminal tabs.

The session starts in the council's working folder; the expert's configuration comes from its
own folder: `--add-dir <expert>` (skills load from there as is; its CLAUDE.md only because the
script sets `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1`) and `--settings <expert>\.claude\settings.json`
when present. The initial prompt goes after `--`: `--add-dir` is variadic and would otherwise
swallow it as one more folder. All of this was verified with the real CLI (`claude -p`).

## Gotchas

- All members of a council must run in the same permission-mode class: if a receiver bypasses
  permissions and the sender doesn't, Claude Code holds incoming messages for approval. That's why
  there is no per-expert permission mode.
- Never launch members with `-p` or `--bare`: they can't show approvals / don't open the inbox.
- `--append-system-prompt-file` is a real flag even though `claude --help` doesn't list it in the
  main options (it's referenced under `--bare`).
- Removing an expert only touches the database, never the folder on disk.
- Expert skills belong in the expert's folder (`ExpertInspector.SkillsDir`), never in the user
  profile, so an expert folder is self-contained and can be versioned in git. Verified: with the
  session started in another folder and `--add-dir DEV-BE`, the skill in
  `DEV-BE\.claude\skills\hello` runs via `/hello`.
- **Tray (`App.xaml.cs`)**: minimizing hides the window and shows a WinForms `NotifyIcon` (only
  while hidden); left click or "Open" restores the previous state, "Exit" closes the window; the X
  quits normally. WinForms is enabled only for this: the .csproj removes the `System.Windows.Forms`
  / `System.Drawing` implicit usings (use the `WinForms` alias) and suppresses WFO0003 (the DPI
  declaration in app.manifest is correct for WPF).
- The icon (`Assets\council.ico`, round table + members, coordinator in yellow) was generated
  programmatically: 16–64 px as 32-bit DIB frames, 128/256 as PNG frames.
- Nullable and ImplicitUsings are both `enable`; with `UseWPF` the implicit usings do not include
  `System.IO`, so files using `Path`/`File` import it explicitly.
