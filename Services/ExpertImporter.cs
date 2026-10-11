using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClaudeCouncilManager.Services;

/// <summary>An expert folder found while scanning a folder to import.</summary>
public sealed record ImportCandidate(string Folder, ExpertManifestData? Manifest, ExpertStatus Status);

/// <summary>Finds expert folders to import, e.g. a set of experts prepared by a colleague.</summary>
public static class ExpertImporter
{
    /// <summary>
    /// The root itself and its direct subfolders that look like experts (a CLAUDE.md or an
    /// expert.json). Dot-folders (.git, .claude, …) are skipped.
    /// </summary>
    public static IReadOnlyList<ImportCandidate> Scan(string root)
    {
        root = Path.TrimEndingDirectorySeparator(root);
        IEnumerable<string> subfolders;
        try
        {
            subfolders = Directory.EnumerateDirectories(root)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            subfolders = Array.Empty<string>();
        }

        return new[] { root }.Concat(subfolders)
            .Where(IsExpertFolder)
            .Select(d => new ImportCandidate(d, ExpertManifest.TryRead(d), ExpertInspector.Inspect(d)))
            .ToList();
    }

    public static bool IsExpertFolder(string folder)
        => ExpertInspector.Inspect(folder).HasClaudeMd || ExpertManifest.Exists(folder);
}
