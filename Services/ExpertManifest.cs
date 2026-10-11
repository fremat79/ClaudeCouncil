using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeCouncilManager.Models;

namespace ClaudeCouncilManager.Services;

/// <summary>The settings an expert folder carries in its <c>expert.json</c>.</summary>
public sealed record ExpertManifestData(
    string? Name, string? Description, string? Color, string? Model, string? Effort, bool IsCoordinator);

/// <summary>
/// Reads and writes <c>expert.json</c>, the expert's settings stored next to its CLAUDE.md so a
/// folder (or a whole set of them in a git repo) can be handed to a colleague and imported as is.
/// The database stays the app's working copy; the manifest is what travels.
/// </summary>
public static class ExpertManifest
{
    public const string FileName = "expert.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep accented text readable
        WriteIndented = true,
    };

    public static bool Exists(string folder) => File.Exists(Path.Combine(folder, FileName));

    /// <summary>The folder's manifest, or null when it is missing or malformed.</summary>
    public static ExpertManifestData? TryRead(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ExpertManifestData>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the expert's settings to its folder unless the file already says the same thing
    /// (so untouched experts never produce git diffs). Returns false when there was nothing to do.
    /// </summary>
    public static bool WriteIfChanged(Expert expert)
    {
        if (!Directory.Exists(expert.Folder)) return false;
        var data = new ExpertManifestData(
            expert.Name, expert.Description, expert.Color, expert.Model, expert.Effort, expert.IsCoordinator);
        var json = JsonSerializer.Serialize(data, Options) + Environment.NewLine;
        var path = Path.Combine(expert.Folder, FileName);
        if (File.Exists(path) && File.ReadAllText(path) == json) return false;
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return true;
    }
}
