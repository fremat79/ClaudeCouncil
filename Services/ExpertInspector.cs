using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeCouncilManager.Services;

public sealed record ExpertStatus(bool FolderExists, string? ClaudeMdPath, IReadOnlyList<string> Skills)
{
    public bool HasClaudeMd => ClaudeMdPath is not null;
}

/// <summary>Reads what Claude Code will find in an expert's folder: CLAUDE.md and project skills.</summary>
public static class ExpertInspector
{
    public static ExpertStatus Inspect(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return new ExpertStatus(false, null, Array.Empty<string>());

        return new ExpertStatus(true, FindClaudeMd(folder), FindSkills(folder));
    }

    /// <summary>Creates a starter CLAUDE.md from the embedded template and returns its path.</summary>
    public static string CreateClaudeMd(string folder, string expertName)
    {
        var path = Path.Combine(folder, "CLAUDE.md");
        if (File.Exists(path))
            throw new InvalidOperationException("This folder already has a CLAUDE.md:\n\n" + path);

        var text = Templates.Load("ExpertClaudeMd.md").Replace("{{NAME}}", expertName);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    // Claude Code accepts project memory both as ./CLAUDE.md and ./.claude/CLAUDE.md.
    private static string? FindClaudeMd(string folder)
        => new[] { Path.Combine(folder, "CLAUDE.md"), Path.Combine(folder, ".claude", "CLAUDE.md") }
            .FirstOrDefault(File.Exists);

    /// <summary>
    /// Project skills live inside the expert's own folder (<c>.claude\skills\&lt;name&gt;\SKILL.md</c>),
    /// never in the user profile, so an expert is self-contained and can be versioned in git.
    /// </summary>
    public static string SkillsDir(string folder) => Path.Combine(folder, ".claude", "skills");

    private static IReadOnlyList<string> FindSkills(string folder)
    {
        var skillsDir = SkillsDir(folder);
        if (!Directory.Exists(skillsDir)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateDirectories(skillsDir)
                .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
