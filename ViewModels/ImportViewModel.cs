using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeCouncilManager.Services;

namespace ClaudeCouncilManager.ViewModels;

/// <summary>Preview of the expert folders found in a folder to import; the user ticks which to register.</summary>
public sealed class ImportViewModel : ObservableObject
{
    public ImportViewModel(string root, IReadOnlyList<ImportCandidate> candidates,
        IReadOnlyList<ExpertViewModel> existing, string? currentCoordinator)
    {
        Root = root;
        var takenNames = existing.Select(e => e.Name).ToList();
        // Who will coordinate after the import: the current one, or the first imported folder flagged as such.
        var coordinator = currentCoordinator;

        foreach (var c in candidates)
        {
            var registered = existing.FirstOrDefault(e => string.Equals(
                Path.TrimEndingDirectorySeparator(e.Folder), Path.TrimEndingDirectorySeparator(c.Folder),
                StringComparison.OrdinalIgnoreCase));

            var notes = new List<string>();
            string name;
            if (registered is not null)
            {
                name = registered.Name;
                notes.Add($"Already registered as {registered.Name}");
            }
            else
            {
                name = MainViewModel.ProposeName(c.Folder, c.Manifest, takenNames);
                takenNames.Add(name);
                var wanted = c.Manifest?.Name ?? Path.GetFileName(c.Folder);
                if (!string.Equals(name, wanted, StringComparison.Ordinal))
                    notes.Add($"The name {wanted} is taken or invalid: it will be imported as {name}");
                if (c.Manifest?.IsCoordinator == true)
                {
                    if (coordinator is null)
                    {
                        coordinator = name;
                        notes.Add("Will become the coordinator");
                    }
                    else
                    {
                        notes.Add($"Coordinator in its source configuration: ignored, the coordinator is {coordinator}");
                    }
                }
                if (c.Manifest is null) notes.Add("No expert.json: default settings");
                if (!c.Status.HasClaudeMd) notes.Add("CLAUDE.md missing");
            }

            Candidates.Add(new ImportCandidateViewModel(this, c, name, string.Join(" · ", notes), registered is null));
        }

        SelectAllCommand = new RelayCommand(_ => SetAll(true));
        SelectNoneCommand = new RelayCommand(_ => SetAll(false));
    }

    public string Root { get; }

    public ObservableCollection<ImportCandidateViewModel> Candidates { get; } = new();

    public int SelectedCount => Candidates.Count(c => c.IsSelected);

    public string ImportButtonText => SelectedCount == 1 ? "Import 1 expert" : $"Import {SelectedCount} experts";

    public bool CanImport => SelectedCount > 0;

    public ICommand SelectAllCommand { get; }

    public ICommand SelectNoneCommand { get; }

    internal void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(CanImport));
    }

    private void SetAll(bool selected)
    {
        foreach (var c in Candidates.Where(c => c.IsSelectable)) c.IsSelected = selected;
    }
}

/// <summary>One expert folder in the import preview.</summary>
public sealed class ImportCandidateViewModel : ObservableObject
{
    private readonly ImportViewModel _owner;
    private bool _isSelected;

    public ImportCandidateViewModel(ImportViewModel owner, ImportCandidate candidate, string name, string note, bool selectable)
    {
        _owner = owner;
        Candidate = candidate;
        Name = name;
        Note = note;
        IsSelectable = selectable;
        _isSelected = selectable;
    }

    public ImportCandidate Candidate { get; }

    public string Folder => Candidate.Folder;

    public ExpertManifestData? Manifest => Candidate.Manifest;

    /// <summary>The name the expert will be registered with (already made unique).</summary>
    public string Name { get; }

    public string Description => Manifest?.Description ?? "";

    public Brush ColorBrush => ExpertViewModel.BrushFromHex(Manifest?.Color);

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Manifest?.Model)) parts.Add(Manifest.Model);
            if (!string.IsNullOrWhiteSpace(Manifest?.Effort)) parts.Add(Manifest.Effort);
            if (parts.Count == 0) parts.Add("default model");
            parts.Add(Candidate.Status.Skills.Count == 0
                ? "no skills"
                : "skills: " + string.Join(", ", Candidate.Status.Skills));
            return string.Join(" · ", parts);
        }
    }

    public string Note { get; }

    public bool IsSelectable { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!IsSelectable || !SetProperty(ref _isSelected, value)) return;
            _owner.SelectionChanged();
        }
    }
}
