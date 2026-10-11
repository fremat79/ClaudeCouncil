using System;

namespace ClaudeCouncilManager.Models;

/// <summary>
/// An expert is a folder on disk holding a CLAUDE.md (and optionally skills under
/// <c>.claude\skills</c> and permissions in <c>.claude\settings.json</c>). Sessions start in the
/// council's working folder and load these via --add-dir / --settings. Stored in SQLite and mirrored to the folder's
/// <c>expert.json</c> (see <c>ExpertManifest</c>) so a set of experts can be shared through git.
/// </summary>
public sealed class Expert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short identifier (e.g. DEV-BE); part of the session name, so restricted to [A-Za-z0-9_-].</summary>
    public string Name { get; set; } = "";

    public string Folder { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Tab / tile color as <c>#RRGGBB</c>.</summary>
    public string? Color { get; set; }

    /// <summary>Value for <c>claude --model</c>; empty means Claude Code's default.</summary>
    public string? Model { get; set; }

    /// <summary>Value for <c>claude --effort</c>; empty means Claude Code's default.</summary>
    public string? Effort { get; set; }

    /// <summary>
    /// The one expert that coordinates every council with two or more experts (added to such
    /// councils automatically). At most one expert has this set.
    /// </summary>
    public bool IsCoordinator { get; set; }

    /// <summary>Personal favorite (listed first). Local to this app: never written to expert.json.</summary>
    public bool IsFavorite { get; set; }
}
