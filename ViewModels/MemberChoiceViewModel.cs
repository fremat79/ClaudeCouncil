namespace ClaudeCouncilManager.ViewModels;

/// <summary>
/// One checkbox in the council editor: "is this expert a member of the council?". The
/// coordinator's box is ticked and locked while it is included automatically.
/// </summary>
public sealed class MemberChoiceViewModel : ObservableObject
{
    private readonly CouncilViewModel _council;

    public MemberChoiceViewModel(CouncilViewModel council, ExpertViewModel expert)
    {
        _council = council;
        Expert = expert;
    }

    public ExpertViewModel Expert { get; }

    public bool IsAutomatic => Expert.IsCoordinator && _council.IsCoordinatorAutoIncluded;

    public bool IsMember
    {
        get => _council.IsPicked(Expert) || IsAutomatic;
        set
        {
            if (IsAutomatic || value == _council.IsPicked(Expert)) return;
            _council.SetMember(Expert, value);
        }
    }

    public bool IsEditable => !IsAutomatic;

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsAutomatic));
        OnPropertyChanged(nameof(IsMember));
        OnPropertyChanged(nameof(IsEditable));
    }
}

/// <summary>
/// A member shown on a council tile; clicking it (re)launches just that member.
/// <see cref="IsRunning"/>: its claude session is open right now.
/// </summary>
public sealed record MemberChip(CouncilViewModel Council, ExpertViewModel Expert, bool IsCoordinator, bool IsRunning);
