using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClaudeCouncilManager.Models;
using ClaudeCouncilManager.Services;

namespace ClaudeCouncilManager.ViewModels;

/// <summary>
/// Editable view of a <see cref="Council"/>. Members are the experts the user picked plus, when
/// two or more are picked, the global coordinator expert, added automatically.
/// </summary>
public sealed class CouncilViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<ExpertViewModel>> _experts;
    private readonly Func<string, bool> _isSessionRunning;
    private readonly Action _changed;
    private string? _error;
    private bool _isLaunching;

    public CouncilViewModel(Council model, Func<IReadOnlyList<ExpertViewModel>> experts,
        Func<string, bool> isSessionRunning, Action changed)
    {
        Model = model;
        _experts = experts;
        _isSessionRunning = isSessionRunning;
        _changed = changed;
        RebuildChoices();
    }

    public Council Model { get; }

    public Guid Id => Model.Id;

    public string Name
    {
        get => Model.Name;
        set
        {
            var v = value.Trim();
            if (v == Model.Name) return;
            Model.Name = v;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>Folder every member's session starts in; changed only through <see cref="SetWorkingFolder"/>.</summary>
    public string WorkingFolder => Model.WorkingFolder;

    /// <summary>
    /// Moves the council to another working folder. Claude Code keeps conversations per start
    /// folder, so the recorded sessions can't be resumed from the new one and are forgotten.
    /// </summary>
    public void SetWorkingFolder(string folder)
    {
        Model.WorkingFolder = folder;
        Model.Sessions.Clear();
        OnPropertyChanged(nameof(WorkingFolder));
        OnPropertyChanged(nameof(CanResume));
        _changed();
    }

    /// <summary>True when at least one member has a recorded session that Resume can reopen.</summary>
    public bool CanResume => Members.Any(m => Model.SessionFor(m.Id) is not null);

    public bool IsFavorite
    {
        get => Model.IsFavorite;
        set
        {
            if (value == Model.IsFavorite) return;
            Model.IsFavorite = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string InitialPrompt
    {
        get => Model.InitialPrompt;
        set
        {
            if (value == Model.InitialPrompt) return;
            Model.InitialPrompt = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string ExtraInstructions
    {
        get => Model.ExtraInstructions;
        set
        {
            if (value == Model.ExtraInstructions) return;
            Model.ExtraInstructions = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>The expert marked as coordinator in the expert list (there is at most one).</summary>
    public ExpertViewModel? GlobalCoordinator => _experts().FirstOrDefault(e => e.IsCoordinator);

    /// <summary>
    /// True when the user picked two or more experts besides the coordinator, so the coordinator
    /// joins automatically and can't be unticked.
    /// </summary>
    public bool IsCoordinatorAutoIncluded
        => GlobalCoordinator is { } coordinator && Picked.Count(e => e != coordinator) >= 2;

    /// <summary>Effective members: the coordinator first (when there is one), then the picked experts.</summary>
    public IReadOnlyList<ExpertViewModel> Members
    {
        get
        {
            var picked = Picked;
            var coordinator = GlobalCoordinator;
            if (coordinator is not null && (IsCoordinatorAutoIncluded || picked.Contains(coordinator)))
                return picked.Where(e => e != coordinator).Prepend(coordinator).ToList();
            return picked;
        }
    }

    /// <summary>The coordinator of this council: only with two or more members, null otherwise.</summary>
    public ExpertViewModel? Coordinator => Members.Count >= 2 ? GlobalCoordinator : null;

    public IReadOnlyList<MemberChip> Chips
    {
        get
        {
            var coordinator = Coordinator;
            return Members
                .Select(m => new MemberChip(this, m, m == coordinator, _isSessionRunning(Naming.SessionName(Name, m.Name))))
                .ToList();
        }
    }

    /// <summary>True when at least one member's session is open right now (Stop is enabled).</summary>
    public bool HasRunningSessions => Chips.Any(c => c.IsRunning);

    /// <summary>True when every member's session is open (Resume has nothing left to reopen).</summary>
    public bool AllSessionsRunning => Chips is { Count: > 0 } chips && chips.All(c => c.IsRunning);

    /// <summary>One line under the member list explaining who coordinates (or why nobody does).</summary>
    public string CoordinatorInfo
    {
        get
        {
            var members = Members;
            if (members.Count == 0) return "";
            if (members.Count == 1)
                return "A single expert: no coordinator; it opens one session, which gets the initial prompt.";
            if (Coordinator is not { } coordinator) return "";
            return $"Coordinator: {coordinator.Name}"
                   + (IsCoordinatorAutoIncluded ? " (added automatically)" : "")
                   + ". It gets the initial prompt, delegates to the members and collects their answers; it starts last.";
        }
    }

    /// <summary>Checkbox list for the editor; rebuilt only when the expert list itself changes.</summary>
    public ObservableCollection<MemberChoiceViewModel> MemberChoices { get; } = new();

    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set => SetProperty(ref _isLaunching, value);
    }

    public bool IsPicked(ExpertViewModel expert) => Model.HasMember(expert.Id);

    public void SetMember(ExpertViewModel expert, bool isMember)
    {
        if (isMember) Model.AddMember(expert.Id);
        else Model.RemoveMember(expert.Id);
        NotifyMembership();
        _changed();
    }

    public void RebuildChoices()
    {
        MemberChoices.Clear();
        foreach (var e in _experts())
            MemberChoices.Add(new MemberChoiceViewModel(this, e));
    }

    /// <summary>Drops ids of experts that no longer exist.</summary>
    public void Prune()
    {
        var known = _experts().Select(e => e.Id).ToHashSet();
        foreach (var id in Model.MemberIds.Where(id => !known.Contains(id)).ToList())
            Model.RemoveMember(id);
        RebuildChoices();
        NotifyMembership();
    }

    /// <summary>Re-raises everything derived from members (picks, coordinator flag, renames).</summary>
    public void NotifyMembership()
    {
        OnPropertyChanged(nameof(Members));
        OnPropertyChanged(nameof(Coordinator));
        OnPropertyChanged(nameof(Chips));
        OnPropertyChanged(nameof(CoordinatorInfo));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(HasRunningSessions));
        OnPropertyChanged(nameof(AllSessionsRunning));
        foreach (var choice in MemberChoices) choice.Refresh();
    }

    private IReadOnlyList<ExpertViewModel> Picked
    {
        get
        {
            var experts = _experts();
            return Model.MemberIds
                .Select(id => experts.FirstOrDefault(e => e.Id == id))
                .OfType<ExpertViewModel>()
                .ToList();
        }
    }
}
