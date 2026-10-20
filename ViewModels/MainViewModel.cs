using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ClaudeCouncilManager.Data;
using ClaudeCouncilManager.Models;
using ClaudeCouncilManager.Services;
using ClaudeCouncilManager.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace ClaudeCouncilManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string AppTitle = "Claude Council Manager";

    // Pause between tabs so the first wt call has registered the named window before the next
    // one targets it, and a longer one before the coordinator so its peers are already reachable.
    private static readonly TimeSpan BetweenTabsDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan CoordinatorDelay = TimeSpan.FromSeconds(3);

    private readonly CouncilDbContext _db = new();
    private readonly DispatcherTimer _saveTimer;
    // Experts edited in the app since the last save: only their expert.json gets rewritten, so a
    // manifest pulled from git is never overwritten by an expert nobody touched.
    private readonly HashSet<Guid> _dirtyManifests = new();
    // Session names of the claude.exe processes running right now (polled, see RefreshRunningAsync).
    private IReadOnlySet<string> _running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _runningTimer;
    private bool _pollingRunning;
    private ExpertViewModel? _selectedExpert;
    private CouncilViewModel? _selectedCouncil;
    private bool _favoriteExpertsOnly;
    private bool _favoriteCouncilsOnly;

    public MainViewModel()
    {
        AppPaths.MigrateLegacyDataFolder();
        Directory.CreateDirectory(AppPaths.AppDataDir);
        _db.Database.Migrate();
        var settings = _db.Settings.Find(AppSettings.SingletonId);
        if (settings is null)
        {
            settings = new AppSettings();
            _db.Settings.Add(settings);
        }
        Settings = new SettingsViewModel(settings, Changed);
        _ = Settings.RecheckClaudeAsync(); // until it answers, launching stays disabled
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => SaveNow();

        var experts = _db.Experts.OrderBy(e => e.Name).ToList();
        // Only one coordinator is allowed; should the database say otherwise, keep the first.
        foreach (var e in experts.Where(e => e.IsCoordinator).Skip(1))
            e.IsCoordinator = false;
        foreach (var e in experts)
            Experts.Add(new ExpertViewModel(e, ExpertEdited, OnBecameCoordinator, Changed));
        foreach (var c in _db.Councils.Include(c => c.Members).Include(c => c.Sessions).OrderBy(c => c.Name).ToList())
            Councils.Add(new CouncilViewModel(c, () => Experts, IsSessionRunning, Changed));
        // Pick up expert.json changes pulled from git since the last run.
        RefreshExperts(adoptManifests: true);
        ExpertsView = FavoritesFirst(Experts);
        CouncilsView = FavoritesFirst(Councils);

        // Keep the "session open" dots on the council tiles in step with reality.
        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _runningTimer.Tick += (_, _) => _ = RefreshRunningAsync();
        _runningTimer.Start();
        _ = RefreshRunningAsync();

        SelectedExpert = Experts.FirstOrDefault();
        SelectedCouncil = Councils.FirstOrDefault();

        AddExpertCommand = new RelayCommand(_ => Run(AddExpert));
        ImportExpertsCommand = new RelayCommand(_ => Run(ImportExperts));
        RemoveExpertCommand = new RelayCommand(p => Run(() => RemoveExpert(p as ExpertViewModel)), p => p is ExpertViewModel);
        ChangeExpertFolderCommand = new RelayCommand(p => Run(() => ChangeExpertFolder((ExpertViewModel)p!)), p => p is ExpertViewModel);
        RefreshExpertsCommand = new RelayCommand(_ => Run(() => RefreshExperts(adoptManifests: true)));
        OpenExpertFolderCommand = new RelayCommand(p => Run(() => OpenExpertFolder((ExpertViewModel)p!)), p => p is ExpertViewModel);
        EditClaudeMdCommand = new RelayCommand(p => Run(() => EditClaudeMd((ExpertViewModel)p!)), p => p is ExpertViewModel { HasClaudeMd: true });
        CreateClaudeMdCommand = new RelayCommand(p => Run(() => CreateClaudeMd((ExpertViewModel)p!)), p => p is ExpertViewModel { FolderExists: true, HasClaudeMd: false });
        OpenSkillsFolderCommand = new RelayCommand(p => Run(() => OpenSkillsFolder((ExpertViewModel)p!)), p => p is ExpertViewModel { FolderExists: true });
        SetColorCommand = new RelayCommand(p => { if (SelectedExpert is not null) SelectedExpert.Color = p as string; });
        SetModelCommand = new RelayCommand(p => { if (SelectedExpert is not null) SelectedExpert.ModelName = p as string; });
        SetEffortCommand = new RelayCommand(p => { if (SelectedExpert is not null) SelectedExpert.Effort = p as string; });

        AddCouncilCommand = new RelayCommand(_ => Run(AddCouncil), _ => NewCouncilBlockedReason is null);
        RemoveCouncilCommand = new RelayCommand(
            p => Run(() => RemoveCouncil(p as CouncilViewModel)), p => p is CouncilViewModel { IsLaunching: false });
        OpenCouncilFolderCommand = new RelayCommand(p => Run(() => OpenCouncilFolder((CouncilViewModel)p!)), p => p is CouncilViewModel { Error: null });
        OpenLogbookCommand = new RelayCommand(p => Run(() => OpenLogbook((CouncilViewModel)p!)), p => p is CouncilViewModel);
        ChangeWorkingFolderCommand = new RelayCommand(p => Run(() => ChangeWorkingFolder((CouncilViewModel)p!)), p => p is CouncilViewModel { IsLaunching: false });
        OpenWorkingFolderCommand = new RelayCommand(p => Run(() => OpenWorkingFolder((CouncilViewModel)p!)), p => p is CouncilViewModel);
        ResumeCouncilCommand = new RelayCommand(
            async p => await RunAsync(() => OpenCouncilAsync((CouncilViewModel)p!, resume: true)),
            // Still useful while some members run: it reopens the ones that are closed.
            p => Settings.IsClaudeUsable && p is CouncilViewModel { IsLaunching: false, CanResume: true, AllSessionsRunning: false });
        LaunchCouncilCommand = new RelayCommand(
            async p => await RunAsync(() => OpenCouncilAsync((CouncilViewModel)p!, resume: false)),
            // New conversations only from a stopped council: with sessions open, ■ them first.
            p => Settings.IsClaudeUsable && p is CouncilViewModel { IsLaunching: false, HasRunningSessions: false });
        TerminateCouncilCommand = new RelayCommand(
            async p => await RunAsync(() => TerminateAsync((CouncilViewModel)p!, onlyExpert: null)),
            p => p is CouncilViewModel { IsLaunching: false, HasRunningSessions: true });
        TerminateMemberCommand = new RelayCommand(
            async p => await RunAsync(() => TerminateAsync(((MemberChip)p!).Council, ((MemberChip)p!).Expert)),
            p => p is MemberChip { IsRunning: true });
        LaunchMemberCommand = new RelayCommand(
            async p => await RunAsync(() => LaunchSingleMemberAsync((MemberChip)p!)), p => Settings.IsClaudeUsable && p is MemberChip);
    }

    public ObservableCollection<ExpertViewModel> Experts { get; } = new();

    public ObservableCollection<CouncilViewModel> Councils { get; } = new();

    /// <summary>The experts as listed on their tab: favorites first, then by name.</summary>
    public ListCollectionView ExpertsView { get; }

    /// <summary>The councils as listed on their tab: favorites first, then by name.</summary>
    public ListCollectionView CouncilsView { get; }

    public bool FavoriteExpertsOnly
    {
        get => _favoriteExpertsOnly;
        set
        {
            if (SetProperty(ref _favoriteExpertsOnly, value))
                ExpertsView.Filter = value ? IsFavoriteItem : null;
        }
    }

    public bool FavoriteCouncilsOnly
    {
        get => _favoriteCouncilsOnly;
        set
        {
            if (SetProperty(ref _favoriteCouncilsOnly, value))
                CouncilsView.Filter = value ? IsFavoriteItem : null;
        }
    }

    private static bool IsFavoriteItem(object item)
        => item is ExpertViewModel { IsFavorite: true } or CouncilViewModel { IsFavorite: true };

    private static ListCollectionView FavoritesFirst(System.Collections.IList items)
    {
        var view = new ListCollectionView(items);
        view.SortDescriptions.Add(new SortDescription(nameof(ExpertViewModel.IsFavorite), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(ExpertViewModel.Name), ListSortDirection.Ascending));
        // Re-sort and re-filter as soon as a heart is toggled. Name isn't live on purpose: cards
        // would jump around while the name is being typed (it applies on load and on insert).
        view.IsLiveSorting = true;
        view.LiveSortingProperties.Add(nameof(ExpertViewModel.IsFavorite));
        view.IsLiveFiltering = true;
        view.LiveFilteringProperties.Add(nameof(ExpertViewModel.IsFavorite));
        return view;
    }

    public ExpertViewModel? SelectedExpert
    {
        get => _selectedExpert;
        set => SetProperty(ref _selectedExpert, value);
    }

    public CouncilViewModel? SelectedCouncil
    {
        get => _selectedCouncil;
        set => SetProperty(ref _selectedCouncil, value);
    }

    /// <summary>
    /// Why "New council" is disabled, or null when it is enabled: creating a council needs the
    /// coordinator expert and at least one other expert.
    /// </summary>
    public string? NewCouncilBlockedReason
    {
        get
        {
            var hasCoordinator = Experts.Any(e => e.IsCoordinator);
            var hasOthers = Experts.Any(e => !e.IsCoordinator);
            return (hasCoordinator, hasOthers) switch
            {
                (true, true) => null,
                (false, false) => "A council needs a coordinator expert and at least one other expert: set them up in the Experts tab.",
                (false, true) => "There is no coordinator: tick 'Council coordinator' on an expert in the Experts tab.",
                (true, false) => "Add at least one expert besides the coordinator in the Experts tab.",
            };
        }
    }

    public string CouncilsEmptyHint => NewCouncilBlockedReason ?? "Create a council with New council.";

    public static IReadOnlyList<string> ModelOptions { get; } = new[] { "", "opus", "sonnet", "haiku", "fable" };

    public static IReadOnlyList<string> EffortOptions { get; } = new[] { "", "low", "medium", "high", "xhigh", "max" };

    public static IReadOnlyList<string> Palette { get; } = new[]
    {
        "#D97757", "#E0A43B", "#5BAA6B", "#3FA7A0", "#4F8FD9", "#7B6CD9", "#B866C9", "#D9578F", "#8A847B",
    };

    public ICommand AddExpertCommand { get; }
    public ICommand ImportExpertsCommand { get; }
    public ICommand RemoveExpertCommand { get; }
    public ICommand ChangeExpertFolderCommand { get; }
    public ICommand RefreshExpertsCommand { get; }
    public ICommand OpenExpertFolderCommand { get; }
    public ICommand EditClaudeMdCommand { get; }
    public ICommand CreateClaudeMdCommand { get; }
    public ICommand OpenSkillsFolderCommand { get; }
    public ICommand SetColorCommand { get; }
    public ICommand SetModelCommand { get; }
    public ICommand SetEffortCommand { get; }
    public ICommand AddCouncilCommand { get; }
    public ICommand RemoveCouncilCommand { get; }
    public ICommand OpenCouncilFolderCommand { get; }
    public ICommand OpenLogbookCommand { get; }
    public ICommand LaunchCouncilCommand { get; }
    public ICommand ResumeCouncilCommand { get; }
    public ICommand ChangeWorkingFolderCommand { get; }
    public ICommand OpenWorkingFolderCommand { get; }
    public ICommand LaunchMemberCommand { get; }
    public ICommand TerminateCouncilCommand { get; }
    public ICommand TerminateMemberCommand { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Saves pending changes to the database, then rewrites expert.json for the experts edited
    /// since the last save. Runs debounced, and once more when the window closes.
    /// </summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _db.SaveChanges();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Can't save to the database:\n\n" + (ex.InnerException ?? ex).Message, AppTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var failed = new List<string>();
        foreach (var e in Experts.Where(e => _dirtyManifests.Contains(e.Id)))
        {
            try
            {
                ExpertManifest.WriteIfChanged(e.Model);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{e.Name}: {ex.Message}");
            }
        }
        _dirtyManifests.Clear();
        if (failed.Count > 0)
            MessageBox.Show("Couldn't update expert.json for:\n\n" + string.Join("\n", failed), AppTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- Experts -------------------------------------------------------------------------

    private void AddExpert()
    {
        var dlg = new OpenFolderDialog { Title = "Choose the expert's folder (with its CLAUDE.md and any skills)" };
        if (dlg.ShowDialog() != true) return;
        var folder = Path.TrimEndingDirectorySeparator(dlg.FolderName);

        var existing = Experts.FirstOrDefault(e => string.Equals(
            Path.TrimEndingDirectorySeparator(e.Folder), folder, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedExpert = existing;
            Info($"This folder is already the expert {existing.Name}.");
            return;
        }

        // A folder prepared by someone else brings its settings in expert.json; otherwise one is
        // created on the next save so the folder becomes shareable too.
        var manifest = ExpertManifest.TryRead(folder);
        var expert = CreateExpert(folder, manifest,
            ProposeName(folder, manifest, Experts.Select(e => e.Name)),
            allowCoordinator: !Experts.Any(e => e.IsCoordinator));
        var vm = Register(expert);
        if (manifest is null) _dirtyManifests.Add(vm.Id);
        SelectedExpert = vm;
        Changed();

        if (!vm.HasClaudeMd && Confirm(
                $"The folder has no CLAUDE.md.\n\nCreate a starter one for {vm.Name}?"))
            CreateClaudeMd(vm);
    }

    private void RemoveExpert(ExpertViewModel? expert)
    {
        if (expert is null) return;
        var usedBy = Councils.Where(c => c.Model.HasMember(expert.Id)).Select(c => c.Name).ToList();
        var msg = $"Remove the expert {expert.Name} from the app?\n\nIts folder is not deleted.";
        if (usedBy.Count > 0) msg += "\n\nIt will also be removed from these councils: " + string.Join(", ", usedBy);
        if (expert.IsCoordinator)
            msg += "\n\nIt is the coordinator: councils with two or more experts can't start until you mark another one.";
        if (!Confirm(msg)) return;

        foreach (var c in Councils) c.Model.RemoveMember(expert.Id);
        _db.Experts.Remove(expert.Model);
        _dirtyManifests.Remove(expert.Id);
        Experts.Remove(expert);
        foreach (var c in Councils) c.Prune();
        SelectedExpert = Experts.FirstOrDefault();
        Changed();
    }

    /// <summary>
    /// Imports experts prepared elsewhere (e.g. a colleague's git repo): the user picks a folder,
    /// reviews the expert folders found in it and ticks the ones to register.
    /// </summary>
    private void ImportExperts()
    {
        var dlg = new OpenFolderDialog { Title = "Folder with the experts to import" };
        if (dlg.ShowDialog() != true) return;

        var candidates = ExpertImporter.Scan(dlg.FolderName);
        if (candidates.Count == 0)
        {
            Info("No experts found in:\n\n" + dlg.FolderName
                 + "\n\nAn expert is a folder (or one of its direct subfolders) with a CLAUDE.md or an expert.json.");
            return;
        }

        var currentCoordinator = Experts.FirstOrDefault(e => e.IsCoordinator)?.Name;
        var preview = new ImportViewModel(dlg.FolderName, candidates, Experts.ToList(), currentCoordinator);
        var window = new ImportWindow { DataContext = preview, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true) return;

        var allowCoordinator = currentCoordinator is null;
        string? newCoordinator = null;
        var imported = new List<ExpertViewModel>();
        foreach (var c in preview.Candidates.Where(c => c.IsSelected))
        {
            var expert = CreateExpert(c.Folder, c.Manifest, c.Name, allowCoordinator);
            if (expert.IsCoordinator)
            {
                allowCoordinator = false;
                newCoordinator = expert.Name;
            }
            imported.Add(Register(expert));
        }
        if (imported.Count == 0) return;

        SelectedExpert = imported[0];
        Changed();
        SaveNow();
        Info($"Imported {imported.Count} experts: {string.Join(", ", imported.Select(e => e.Name))}."
             + (newCoordinator is null ? "" : $"\n\nCoordinator: {newCoordinator}."));
    }

    /// <summary>Default name for a folder: the one in its expert.json if valid, else the folder name; made unique.</summary>
    internal static string ProposeName(string folder, ExpertManifestData? manifest, IEnumerable<string> taken)
        => Naming.Unique(
            manifest?.Name is { } n && Naming.IsValid(n) ? n : Naming.Sanitize(Path.GetFileName(folder)), taken);

    private Expert CreateExpert(string folder, ExpertManifestData? manifest, string name, bool allowCoordinator) => new()
    {
        Folder = Path.TrimEndingDirectorySeparator(folder),
        Name = name,
        Description = manifest?.Description ?? "",
        Color = Naming.IsHexColor(manifest?.Color) ? manifest!.Color : Palette[Experts.Count % Palette.Count],
        Model = string.IsNullOrWhiteSpace(manifest?.Model) ? null : manifest.Model.Trim(),
        Effort = string.IsNullOrWhiteSpace(manifest?.Effort) ? null : manifest.Effort.Trim(),
        // The coordinator flag of a shared folder is honoured only while there is no coordinator yet.
        IsCoordinator = allowCoordinator && manifest?.IsCoordinator == true,
    };

    private ExpertViewModel Register(Expert expert)
    {
        _db.Experts.Add(expert);
        var vm = new ExpertViewModel(expert, ExpertEdited, OnBecameCoordinator, Changed);
        Experts.Add(vm);
        foreach (var c in Councils) c.RebuildChoices();
        return vm;
    }

    private void ChangeExpertFolder(ExpertViewModel expert)
    {
        var dlg = new OpenFolderDialog { Title = $"New folder for {expert.Name}" };
        if (Directory.Exists(expert.Folder)) dlg.InitialDirectory = expert.Folder;
        if (dlg.ShowDialog() != true) return;
        var folder = Path.TrimEndingDirectorySeparator(dlg.FolderName);
        expert.SetFolder(folder);
        // Adopt the settings of a folder that already describes itself; otherwise write ours there.
        if (ExpertManifest.TryRead(folder) is { } manifest)
            expert.ApplyManifest(manifest, name => IsNameFree(name, expert));
        else
            _dirtyManifests.Add(expert.Id);
        Changed();
    }

    /// <summary>
    /// Re-reads each expert's folder (CLAUDE.md, skills) and, with <paramref name="adoptManifests"/>,
    /// the settings in its expert.json, skipping experts with unsaved edits.
    /// </summary>
    private void RefreshExperts(bool adoptManifests)
    {
        var adopted = false;
        foreach (var e in Experts)
        {
            e.RefreshStatus();
            if (adoptManifests && !_dirtyManifests.Contains(e.Id) && ExpertManifest.TryRead(e.Folder) is { } manifest)
                adopted |= e.ApplyManifest(manifest, name => IsNameFree(name, e));
        }
        Revalidate();
        foreach (var c in Councils) c.NotifyMembership();
        if (adopted) ScheduleSave();
    }

    private bool IsNameFree(string name, ExpertViewModel self)
        => !Experts.Any(o => o != self && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void OpenExpertFolder(ExpertViewModel expert)
    {
        if (!Directory.Exists(expert.Folder))
            throw new DirectoryNotFoundException("This folder no longer exists:\n\n" + expert.Folder);
        Process.Start(new ProcessStartInfo { FileName = expert.Folder, UseShellExecute = true });
    }

    private static void EditClaudeMd(ExpertViewModel expert)
    {
        if (expert.ClaudeMdPath is { } path) MarkdownWindow.Open(path, MarkdownViewMode.Split, $"{expert.Name} · CLAUDE.md");
    }

    /// <summary>Opens (creating it if needed) the expert's own skills folder in Explorer.</summary>
    private static void OpenSkillsFolder(ExpertViewModel expert)
    {
        var dir = expert.SkillsFolder;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void CreateClaudeMd(ExpertViewModel expert)
    {
        var path = ExpertInspector.CreateClaudeMd(expert.Folder, expert.Name);
        expert.RefreshStatus();
        Revalidate();
        MarkdownWindow.Open(path, MarkdownViewMode.Split, $"{expert.Name} · CLAUDE.md");
    }

    // ---- Councils ------------------------------------------------------------------------

    /// <summary>A council needs its working folder from the start; its name defaults to the folder's.</summary>
    private void AddCouncil()
    {
        var dlg = new OpenFolderDialog { Title = "Working folder of the new council (where all its sessions start)" };
        if (dlg.ShowDialog() != true) return;
        var folder = Path.TrimEndingDirectorySeparator(dlg.FolderName);
        var leaf = Path.GetFileName(folder);

        var council = new Council
        {
            Name = Naming.Unique(string.IsNullOrEmpty(leaf) ? "COUNCIL" : Naming.Sanitize(leaf), Councils.Select(c => c.Name)),
            WorkingFolder = folder,
        };
        _db.Councils.Add(council);
        var vm = new CouncilViewModel(council, () => Experts, IsSessionRunning, Changed);
        Councils.Add(vm);
        SelectedCouncil = vm;
        Changed();
    }

    /// <summary>
    /// Deletes the council (and its member rows) from the database. Experts, their folders, the
    /// council's runtime folder and any session still open are left alone.
    /// </summary>
    private void RemoveCouncil(CouncilViewModel? council)
    {
        if (council is null) return;

        var msg = $"Delete the council {council.Name}?\n\nThe experts and their folders are not touched.";
        var open = council.Chips.Where(c => c.IsRunning).Select(c => Naming.SessionName(council.Name, c.Expert.Name)).ToList();
        if (open.Count > 0)
            msg += "\n\nThese sessions are still running and won't be stopped:\n\n" + string.Join("\n", open);
        if (!Confirm(msg)) return;

        var index = Councils.IndexOf(council);
        _db.Councils.Remove(council.Model);
        Councils.Remove(council);
        SelectedCouncil = Councils.Count == 0 ? null : Councils[Math.Min(index, Councils.Count - 1)];
        Changed();
        SaveNow();
    }

    private static void OpenCouncilFolder(CouncilViewModel council)
    {
        var dir = AppPaths.CouncilSharedDir(council.Name);
        Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(dir)!, UseShellExecute = true });
    }

    /// <summary>Opens the logbook the coordinator keeps; it opens at the bottom, where new entries are added.</summary>
    private static void OpenLogbook(CouncilViewModel council)
    {
        var path = AppPaths.CouncilLogbook(council.Name);
        if (!File.Exists(path))
        {
            Info($"Council {council.Name} has no logbook yet.\n\n"
                 + "The coordinator starts it in the shared folder with the first goal it gets "
                 + "(councils with two or more experts).");
            return;
        }
        MarkdownWindow.Open(path, MarkdownViewMode.Preview, $"{council.Name} · logbook", scrollToEnd: true);
    }

    private void ChangeWorkingFolder(CouncilViewModel council)
    {
        var dlg = new OpenFolderDialog { Title = $"Working folder of {council.Name}" };
        if (Directory.Exists(council.WorkingFolder)) dlg.InitialDirectory = council.WorkingFolder;
        if (dlg.ShowDialog() != true) return;
        var folder = Path.TrimEndingDirectorySeparator(dlg.FolderName);
        if (string.Equals(folder, council.WorkingFolder, StringComparison.OrdinalIgnoreCase)) return;
        if (council.Model.Sessions.Count > 0 && !Confirm(
                "The sessions already started belong to the current folder: with a new folder, Resume can't reopen them.\n\nChange the folder?"))
            return;
        council.SetWorkingFolder(folder);
    }

    private static void OpenWorkingFolder(CouncilViewModel council)
    {
        if (!Directory.Exists(council.WorkingFolder))
            throw new DirectoryNotFoundException("The working folder doesn't exist:\n\n" + council.WorkingFolder);
        Process.Start(new ProcessStartInfo { FileName = council.WorkingFolder, UseShellExecute = true });
    }

    /// <summary>
    /// Opens the council's sessions that aren't already running, from its working folder.
    /// Start (<paramref name="resume"/> false) starts new conversations; a council started from
    /// scratch sends the initial prompt, filling in members whose tab was closed doesn't.
    /// Resume reopens each member's last conversation with --resume (a member without one
    /// starts a new conversation) and never sends the initial prompt.
    /// </summary>
    private async Task OpenCouncilAsync(CouncilViewModel council, bool resume)
    {
        if (!IsLaunchable(council) || !await IsClaudeReadyAsync()) return;

        council.IsLaunching = true;
        try
        {
            var members = council.Members;
            var running = await RefreshRunningAsync(force: true);
            var open = members.Where(m => running.Contains(Naming.SessionName(council.Name, m.Name))).ToList();
            var toLaunch = members.Except(open).ToList();
            if (toLaunch.Count == 0)
            {
                Info($"All the sessions of council {council.Name} are already running.");
                return;
            }

            var resumable = resume ? toLaunch.Where(CanResume).ToHashSet() : new HashSet<ExpertViewModel>();
            var fresh = !resume && open.Count == 0;

            var notes = new List<string>();
            if (open.Count > 0)
                notes.Add("Already running, left as they are:\n\n" + SessionList(council, open));
            if (resume && resumable.Count < toLaunch.Count)
                notes.Add("No conversation to resume, so a new one starts:\n\n"
                          + SessionList(council, toLaunch.Where(m => !resumable.Contains(m))));
            if (fresh && council.CanResume)
                notes.Add("This council already has conversations: Start begins new ones "
                          + "(the previous ones stay in Claude Code's history). To continue the work, use Resume.");
            if (notes.Count > 0 && !Confirm(string.Join("\n\n", notes)
                    + (resume || fresh ? "\n\nContinue?" : "\n\nStart only the missing sessions? The initial prompt is not sent again.")))
                return;

            // The coordinator, or the only expert of a single-expert council, gets the initial prompt.
            var lead = council.Coordinator ?? members[0];
            // Members first, lead last: when its first turn starts, its peers are already up.
            var ordered = toLaunch.Where(m => m != lead).Concat(toLaunch.Where(m => m == lead)).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var m = ordered[i];
                if (i > 0) await Task.Delay(m == lead ? CoordinatorDelay : BetweenTabsDelay);
                LaunchMember(council, m, resume: resumable.Contains(m), withPrompt: fresh && m == lead);
            }
        }
        finally
        {
            council.IsLaunching = false;
            council.NotifyMembership();
            SaveNow(); // the new session ids must survive even if the app is closed right away
            CommandManager.InvalidateRequerySuggested();
        }

        bool CanResume(ExpertViewModel m) => council.Model.SessionFor(m.Id) is { } id && SessionHistory.Exists(id);
    }

    /// <summary>Clicking a closed member reopens its last conversation, or starts a new one if there is none.</summary>
    private async Task LaunchSingleMemberAsync(MemberChip chip)
    {
        var council = chip.Council;
        if (!IsLaunchable(council) || !await IsClaudeReadyAsync()) return;

        var sessionName = Naming.SessionName(council.Name, chip.Expert.Name);
        if ((await RefreshRunningAsync(force: true)).Contains(sessionName))
        {
            Info($"Session {sessionName} is already running.");
            return;
        }
        var resume = council.Model.SessionFor(chip.Expert.Id) is { } id && SessionHistory.Exists(id);
        // A new conversation for one member of a larger council is a recovery: no kickoff prompt.
        LaunchMember(council, chip.Expert, resume, withPrompt: !resume && council.Members.Count == 1);
        council.NotifyMembership();
        SaveNow();
    }

    /// <summary>
    /// Re-checks Claude Code just before launching (it may have been uninstalled or updated since
    /// the last check); councils can be configured without it, but not launched.
    /// </summary>
    private async Task<bool> IsClaudeReadyAsync()
    {
        await Settings.RecheckClaudeAsync();
        if (Settings.IsClaudeUsable) return true;
        Info("Councils can't be started:\n\n" + Settings.ClaudeProblem);
        return false;
    }

    private bool IsLaunchable(CouncilViewModel council)
    {
        if (Settings.Error is { } settingsError)
        {
            Info("The launch settings are not valid (Settings tab):\n\n" + settingsError);
            return false;
        }
        RefreshExperts(adoptManifests: false);
        if (council.Error is null) return true;
        Info($"Council {council.Name} can't be started:\n\n{council.Error}");
        return false;
    }

    private static string SessionList(CouncilViewModel council, IEnumerable<ExpertViewModel> members)
        => string.Join("\n", members.Select(m => Naming.SessionName(council.Name, m.Name)));

    public bool IsSessionRunning(string sessionName) => _running.Contains(sessionName);

    /// <summary>
    /// Re-reads which sessions are running (WMI, off the UI thread) and refreshes the council
    /// tiles if that changed. The periodic poll skips a round while one is in flight; launches pass
    /// <paramref name="force"/> so they always decide on a fresh reading.
    /// </summary>
    private async Task<IReadOnlySet<string>> RefreshRunningAsync(bool force = false)
    {
        if (_pollingRunning && !force) return _running;
        _pollingRunning = true;
        try
        {
            var now = await Task.Run(SessionMonitor.RunningSessionNames);
            if (!now.SetEquals(_running))
            {
                _running = now;
                foreach (var c in Councils) c.NotifyMembership();
                // The poll runs without user input, so command states must be re-queried explicitly.
                CommandManager.InvalidateRequerySuggested();
            }
            return now;
        }
        finally
        {
            _pollingRunning = false;
        }
    }

    private void LaunchMember(CouncilViewModel council, ExpertViewModel member, bool resume, bool withPrompt)
    {
        Guid sessionId;
        if (resume)
        {
            sessionId = council.Model.SessionFor(member.Id)!.Value;
        }
        else
        {
            sessionId = Guid.NewGuid();
            council.Model.SetSession(member.Id, sessionId);
        }

        var members = council.Members;
        var coordinator = council.Coordinator; // non-null whenever members > 1 (IsLaunchable validated it)
        var multi = coordinator is not null;
        var sessionName = Naming.SessionName(council.Name, member.Name);

        string? protocolFile = null;
        string? sharedDir = null;
        if (coordinator is not null)
        {
            sharedDir = AppPaths.CouncilSharedDir(council.Name);
            protocolFile = Path.Combine(AppPaths.CouncilRuntimeDir(council.Name), member.Name + ".protocol.md");
            var protocol = ProtocolBuilder.Build(
                council.Model, member.Model, members.Select(m => m.Model).ToList(), coordinator.Model, sharedDir);
            File.WriteAllText(protocolFile, protocol, new UTF8Encoding(false));
        }

        // The session starts in the council's folder; the expert's CLAUDE.md and skills come from
        // its own folder via --add-dir, its permissions from its .claude\settings.json.
        var settings = Path.Combine(member.Folder, ".claude", "settings.json");
        var addDirs = new List<string> { member.Folder };
        if (sharedDir is not null) addDirs.Add(sharedDir);

        SessionLauncher.Launch(new LaunchRequest(
            ClaudePath: Settings.ClaudeExecutable!,
            CouncilName: council.Name,
            ExpertName: member.Name,
            TabTitle: multi && member == coordinator ? member.Name + " ★" : member.Name,
            TabColor: member.Color,
            WorkingDirectory: council.WorkingFolder,
            SessionName: sessionName,
            SessionId: sessionId,
            Resume: resume,
            Model: member.ModelName,
            Effort: member.Effort,
            SettingsFile: File.Exists(settings) ? settings : null,
            AddDirs: addDirs,
            AppendSystemPromptFile: protocolFile,
            InitialPrompt: withPrompt && !resume ? council.InitialPrompt : null,
            StopFile: AppPaths.StopFile(council.Name, member.Name)), Settings.Model);
    }

    /// <summary>
    /// Ends the council's running sessions (every <c>COUNCIL.*</c> session, so also members since
    /// unticked), or just <paramref name="onlyExpert"/>'s. The stop file written first tells each
    /// session's script that the exit was requested, so its tab/window closes instead of waiting
    /// on the error. Conversations stay saved and can be reopened with Resume.
    /// </summary>
    private async Task TerminateAsync(CouncilViewModel council, ExpertViewModel? onlyExpert)
    {
        var prefix = council.Name + ".";
        var sessions = (await Task.Run(SessionMonitor.RunningSessions))
            .Where(s => onlyExpert is null
                ? s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : string.Equals(s.Name, Naming.SessionName(council.Name, onlyExpert.Name), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sessions.Count == 0)
        {
            Info("There are no running sessions to stop.");
            await RefreshRunningAsync(force: true);
            return;
        }

        var names = sessions.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dialog = new TerminateWindow(names, Settings.Model.CloseTerminalOnTerminate) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        var closeTerminal = dialog.CloseTerminal;
        if (closeTerminal != Settings.Model.CloseTerminalOnTerminate)
        {
            Settings.Model.CloseTerminalOnTerminate = closeTerminal; // remembered for next time
            Changed();
        }

        foreach (var session in sessions)
        {
            // Read by the session's script once claude is gone: "close" exits, "keep" stays at the prompt.
            File.WriteAllText(AppPaths.StopFile(council.Name, session.Name[prefix.Length..]), closeTerminal ? "close" : "keep");
            await Task.Run(() => SessionMonitor.Terminate(session, closeTerminal));
        }
        await RefreshRunningAsync(force: true);
    }

    // ---- Validation & persistence --------------------------------------------------------

    private void Changed()
    {
        Revalidate();
        // Expert renames/recolors show up on council tiles.
        foreach (var c in Councils) c.NotifyMembership();
        // Adding/removing experts or moving the coordinator flag can enable "New council".
        OnPropertyChanged(nameof(NewCouncilBlockedReason));
        OnPropertyChanged(nameof(CouncilsEmptyHint));
        CommandManager.InvalidateRequerySuggested();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>The user changed a setting of this expert: its expert.json is rewritten on the next save.</summary>
    private void ExpertEdited(ExpertViewModel expert)
    {
        _dirtyManifests.Add(expert.Id);
        Changed();
    }

    private void Revalidate()
    {
        foreach (var e in Experts)
        {
            e.Error = !Naming.IsValid(e.Name) ? "Invalid name: use only letters, digits, - and _ (max 40)."
                : Experts.Count(o => string.Equals(o.Name, e.Name, StringComparison.OrdinalIgnoreCase)) > 1
                    ? "Name already used by another expert."
                : !e.FolderExists ? "Folder not found: " + e.Folder
                : !string.IsNullOrEmpty(e.Effort) && !EffortOptions.Contains(e.Effort)
                    ? "Invalid effort: use " + string.Join(", ", EffortOptions.Skip(1)) + " or leave it empty."
                : null;
        }

        foreach (var c in Councils)
        {
            var members = c.Members;
            c.Error = !Naming.IsValid(c.Name) ? "Invalid name: use only letters, digits, - and _ (max 40)."
                : Councils.Count(o => string.Equals(o.Name, c.Name, StringComparison.OrdinalIgnoreCase)) > 1
                    ? "Name already used by another council."
                : string.IsNullOrWhiteSpace(c.WorkingFolder) ? "Choose the council's working folder (where all its sessions start)."
                : !Directory.Exists(c.WorkingFolder) ? "Working folder not found: " + c.WorkingFolder
                : members.Count == 0 ? "Add at least one expert."
                : members.Count > 1 && c.Coordinator is null
                    ? "Two or more experts need the coordinator: mark an expert as coordinator in the Experts tab."
                : members.FirstOrDefault(m => m.Error is not null) is { } bad ? $"Expert {bad.Name}: {bad.Error}"
                : null;
        }
    }

    /// <summary>Only one expert can be coordinator: clear the flag everywhere else.</summary>
    private void OnBecameCoordinator(ExpertViewModel coordinator)
    {
        foreach (var e in Experts)
            if (e != coordinator && e.ClearCoordinator())
                _dirtyManifests.Add(e.Id);
    }


    // ---- Shell helpers -------------------------------------------------------------------

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void Info(string message)
        => MessageBox.Show(message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);

    private static bool Confirm(string message)
        => MessageBox.Show(message, AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
