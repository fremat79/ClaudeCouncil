using System;
using System.IO;
using System.Linq;

namespace ClaudeCouncilManager.Services;

/// <summary>
/// Claude Code keeps each conversation as <c>~\.claude\projects\&lt;encoded start folder&gt;\&lt;session id&gt;.jsonl</c>.
/// Checking for that file tells whether a session can still be reopened with <c>--resume</c>.
/// </summary>
public static class SessionHistory
{
    private static readonly string ProjectsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public static bool Exists(Guid sessionId)
    {
        var file = sessionId.ToString() + ".jsonl";
        try
        {
            // The folder name encodes the start folder; searching all of them avoids re-deriving it.
            return Directory.Exists(ProjectsRoot)
                   && Directory.EnumerateDirectories(ProjectsRoot).Any(d => File.Exists(Path.Combine(d, file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
