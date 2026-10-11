using System;
using System.Collections.Generic;
using System.Windows.Media;
using ClaudeCouncilManager.Models;
using ClaudeCouncilManager.Services;

namespace ClaudeCouncilManager.ViewModels;

/// <summary>Editable view of an <see cref="Expert"/> plus what was found in its folder.</summary>
public sealed class ExpertViewModel : ObservableObject
{
    private readonly Action<ExpertViewModel> _edited;
    private readonly Action<ExpertViewModel> _becameCoordinator;
    private readonly Action _changedLocally;
    private ExpertStatus _status = new(false, null, Array.Empty<string>());
    private string? _error;

    /// <param name="edited">Called after the user changed a setting (the expert's expert.json gets rewritten).</param>
    /// <param name="becameCoordinator">Called when this expert is made coordinator, to clear the flag elsewhere.</param>
    /// <param name="changedLocally">Called for app-only changes (favorite) that must stay out of expert.json.</param>
    public ExpertViewModel(Expert model, Action<ExpertViewModel> edited, Action<ExpertViewModel> becameCoordinator,
        Action changedLocally)
    {
        Model = model;
        _edited = edited;
        _becameCoordinator = becameCoordinator;
        _changedLocally = changedLocally;
        RefreshStatus();
    }

    public Expert Model { get; }

    public Guid Id => Model.Id;

    /// <summary>Personal favorite: saved in the database only, never in the shared expert.json.</summary>
    public bool IsFavorite
    {
        get => Model.IsFavorite;
        set
        {
            if (value == Model.IsFavorite) return;
            Model.IsFavorite = value;
            OnPropertyChanged();
            _changedLocally();
        }
    }

    public string Name
    {
        get => Model.Name;
        set => Set(Model.Name, value.Trim(), v => Model.Name = v);
    }

    public string Folder => Model.Folder;

    public string SkillsFolder => ExpertInspector.SkillsDir(Model.Folder);

    public string Description
    {
        get => Model.Description;
        set => Set(Model.Description, value, v => Model.Description = v);
    }

    public string? Color
    {
        get => Model.Color;
        set
        {
            if (Set(Model.Color, value?.Trim(), v => Model.Color = v))
                OnPropertyChanged(nameof(ColorBrush));
        }
    }

    public string? ModelName
    {
        get => Model.Model;
        set
        {
            if (Set(Model.Model, value?.Trim(), v => Model.Model = v))
                OnPropertyChanged(nameof(Settings));
        }
    }

    public string? Effort
    {
        get => Model.Effort;
        set
        {
            if (Set(Model.Effort, value?.Trim(), v => Model.Effort = v))
                OnPropertyChanged(nameof(Settings));
        }
    }

    /// <summary>Only one expert can be coordinator: setting it here clears it on the previous one.</summary>
    public bool IsCoordinator
    {
        get => Model.IsCoordinator;
        set
        {
            if (value == Model.IsCoordinator) return;
            Model.IsCoordinator = value;
            OnPropertyChanged();
            if (value) _becameCoordinator(this);
            _edited(this);
        }
    }

    /// <summary>Called when another expert becomes coordinator; returns true if the flag was set.</summary>
    public bool ClearCoordinator()
    {
        if (!Model.IsCoordinator) return false;
        Model.IsCoordinator = false;
        OnPropertyChanged(nameof(IsCoordinator));
        return true;
    }

    public Brush ColorBrush => BrushFromHex(Model.Color);

    /// <summary>"opus · high", or a hint that Claude Code's defaults apply.</summary>
    public string Settings
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Model.Model)) parts.Add(Model.Model);
            if (!string.IsNullOrWhiteSpace(Model.Effort)) parts.Add(Model.Effort);
            return parts.Count == 0 ? "modello predefinito" : string.Join(" · ", parts);
        }
    }

    public bool FolderExists => _status.FolderExists;

    public bool HasClaudeMd => _status.HasClaudeMd;

    public string? ClaudeMdPath => _status.ClaudeMdPath;

    public IReadOnlyList<string> Skills => _status.Skills;

    public string SkillsText => _status.Skills.Count == 0 ? "nessuna skill" : "skill: " + string.Join(", ", _status.Skills);

    public string StatusText => !_status.FolderExists
        ? "Cartella non trovata"
        : _status.HasClaudeMd ? "CLAUDE.md ✓ · " + SkillsText : "CLAUDE.md mancante · " + SkillsText;

    /// <summary>Set by <see cref="MainViewModel"/> validation (needs the global view for uniqueness).</summary>
    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public void SetFolder(string folder)
    {
        Model.Folder = folder;
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(SkillsFolder));
        RefreshStatus();
    }

    /// <summary>
    /// Adopts the settings found in the folder's expert.json (e.g. after a git pull of a colleague's
    /// changes). The name is taken only if valid and free; the coordinator flag is a local choice
    /// and is only honoured at import. Returns true if anything changed.
    /// </summary>
    public bool ApplyManifest(ExpertManifestData manifest, Func<string, bool> isNameFree)
    {
        var changed = false;
        if (manifest.Name is { } name && name != Model.Name && Naming.IsValid(name) && isNameFree(name))
        {
            Model.Name = name;
            changed = true;
        }
        if ((manifest.Description ?? "") != Model.Description)
        {
            Model.Description = manifest.Description ?? "";
            changed = true;
        }
        if (Naming.IsHexColor(manifest.Color) && manifest.Color != Model.Color)
        {
            Model.Color = manifest.Color;
            changed = true;
        }
        if (Normalize(manifest.Model) != Normalize(Model.Model))
        {
            Model.Model = Normalize(manifest.Model);
            changed = true;
        }
        if (Normalize(manifest.Effort) != Normalize(Model.Effort))
        {
            Model.Effort = Normalize(manifest.Effort);
            changed = true;
        }

        if (changed)
        {
            foreach (var p in new[] { nameof(Name), nameof(Description), nameof(Color), nameof(ColorBrush),
                                      nameof(ModelName), nameof(Effort), nameof(Settings) })
                OnPropertyChanged(p);
        }
        return changed;
    }

    public void RefreshStatus()
    {
        _status = ExpertInspector.Inspect(Model.Folder);
        OnPropertyChanged(nameof(FolderExists));
        OnPropertyChanged(nameof(HasClaudeMd));
        OnPropertyChanged(nameof(ClaudeMdPath));
        OnPropertyChanged(nameof(Skills));
        OnPropertyChanged(nameof(SkillsText));
        OnPropertyChanged(nameof(StatusText));
    }

    private bool Set<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        OnPropertyChanged(name);
        _edited(this);
        return true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static Brush BrushFromHex(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // Half-typed or invalid color: fall back to gray.
        }
        return Brushes.Gray;
    }
}
