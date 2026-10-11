using System;

namespace ClaudeCouncilManager.Models;

/// <summary>Join row between a council and an expert the user picked for it.</summary>
public sealed class CouncilMember
{
    public Guid CouncilId { get; set; }

    public Guid ExpertId { get; set; }

    /// <summary>Position in the council, so members keep the order they were picked in.</summary>
    public int Order { get; set; }
}
