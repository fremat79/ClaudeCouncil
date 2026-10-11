using System.Collections.Generic;
using System.Text;
using ClaudeCouncilManager.Models;

namespace ClaudeCouncilManager.Services;

/// <summary>
/// Renders the council protocol a member receives via <c>--append-system-prompt-file</c>:
/// who it is, who the other members are (with their session addresses), who coordinates,
/// and the messaging rules. The expert's own instructions stay in its CLAUDE.md.
/// </summary>
public static class ProtocolBuilder
{
    public static string Build(
        Council council, Expert self, IReadOnlyList<Expert> members, Expert coordinator, string sharedDir)
    {
        var coordinatorAddress = Naming.SessionName(council.Name, coordinator.Name);
        var isCoordinator = self.Id == coordinator.Id;

        var roleSection = Templates.Load(isCoordinator ? "CoordinatorSection.md" : "MemberSection.md");

        var extra = string.IsNullOrWhiteSpace(council.ExtraInstructions)
            ? ""
            : "\n## Council-specific instructions\n\n" + council.ExtraInstructions.Trim() + "\n";

        return Templates.Load("CouncilProtocol.md")
            .Replace("{{ROLE_SECTION}}", roleSection)
            .Replace("{{EXTRA}}", extra)
            .Replace("{{MEMBERS}}", MemberList(council, self, members, coordinator))
            .Replace("{{COUNCIL}}", council.Name)
            .Replace("{{SELF_NAME}}", self.Name)
            .Replace("{{SELF_ADDRESS}}", Naming.SessionName(council.Name, self.Name))
            .Replace("{{COORDINATOR_ADDRESS}}", coordinatorAddress)
            .Replace("{{WORK_DIR}}", council.WorkingFolder)
            .Replace("{{SHARED_DIR}}", sharedDir);
    }

    private static string MemberList(Council council, Expert self, IReadOnlyList<Expert> members, Expert coordinator)
    {
        var sb = new StringBuilder();
        foreach (var m in members)
        {
            sb.Append("- `").Append(Naming.SessionName(council.Name, m.Name)).Append("` - **").Append(m.Name).Append("**");
            if (m.Id == coordinator.Id) sb.Append(" (coordinator)");
            if (m.Id == self.Id) sb.Append(" (you)");
            if (!string.IsNullOrWhiteSpace(m.Description))
                sb.Append(": ").Append(m.Description.Trim().ReplaceLineEndings(" "));
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
}
