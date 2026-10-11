using System;

namespace ClaudeCouncilManager.Models;

/// <summary>
/// The last Claude Code session started for one member of a council, so Resume can reopen
/// that conversation with <c>claude --resume</c> from the council's working folder.
/// </summary>
public sealed class CouncilSession
{
    public Guid CouncilId { get; set; }

    public Guid ExpertId { get; set; }

    /// <summary>The id passed as <c>--session-id</c>; Claude Code names the transcript after it.</summary>
    public Guid SessionId { get; set; }

    public DateTime StartedAtUtc { get; set; }
}
