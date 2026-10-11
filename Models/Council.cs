using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCouncilManager.Models;

/// <summary>
/// A council is a named group of experts launched together as peer Claude Code sessions.
/// With two or more experts the coordinator expert (see <see cref="Expert.IsCoordinator"/>)
/// joins automatically and the others report to it; a single expert runs as a plain session.
/// </summary>
public sealed class Council
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Prefix of every member's session name (<c>NAME.EXPERT</c>); restricted to [A-Za-z0-9_-].</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Folder every member's session starts in (the project the council works on). Claude Code
    /// stores conversations per start folder, so this is also what makes <c>--resume</c> work.
    /// </summary>
    public string WorkingFolder { get; set; } = "";

    /// <summary>First message sent to the coordinator (or to the only member) at launch.</summary>
    public string InitialPrompt { get; set; } = "";

    /// <summary>Free text appended to the generated council protocol.</summary>
    public string ExtraInstructions { get; set; } = "";

    /// <summary>Personal favorite: listed first on the councils tab.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Experts picked by the user; the coordinator is not stored here when added automatically.</summary>
    public List<CouncilMember> Members { get; set; } = new();

    /// <summary>Picked expert ids in the order they were added.</summary>
    public IEnumerable<Guid> MemberIds => Members.OrderBy(m => m.Order).Select(m => m.ExpertId);

    public bool HasMember(Guid expertId) => Members.Any(m => m.ExpertId == expertId);

    public void AddMember(Guid expertId)
    {
        if (HasMember(expertId)) return;
        var order = Members.Count == 0 ? 0 : Members.Max(m => m.Order) + 1;
        Members.Add(new CouncilMember { CouncilId = Id, ExpertId = expertId, Order = order });
    }

    /// <summary>EF deletes the row: a member removed from the collection is an orphan.</summary>
    public void RemoveMember(Guid expertId) => Members.RemoveAll(m => m.ExpertId == expertId);

    /// <summary>Last session started for each member (kept even if the member is unticked later).</summary>
    public List<CouncilSession> Sessions { get; set; } = new();

    public Guid? SessionFor(Guid expertId) => Sessions.FirstOrDefault(s => s.ExpertId == expertId)?.SessionId;

    public void SetSession(Guid expertId, Guid sessionId)
    {
        var row = Sessions.FirstOrDefault(s => s.ExpertId == expertId);
        if (row is null)
        {
            Sessions.Add(new CouncilSession
            {
                CouncilId = Id, ExpertId = expertId, SessionId = sessionId, StartedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            row.SessionId = sessionId;
            row.StartedAtUtc = DateTime.UtcNow;
        }
    }
}
