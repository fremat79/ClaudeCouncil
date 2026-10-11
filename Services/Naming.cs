using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeCouncilManager.Services;

/// <summary>
/// Expert and council names end up in session names (the address peers use with
/// SendMessage), Windows Terminal window names and runtime folder names, so they are kept
/// to a conservative character set.
/// </summary>
public static partial class Naming
{
    public const int MaxLength = 40;

    /// <summary>Joins council and expert into the session name, e.g. <c>FEATURE-X.DEV-BE</c>.</summary>
    public static string SessionName(string council, string expert) => $"{council}.{expert}";

    public static bool IsValid(string? name)
        => !string.IsNullOrEmpty(name) && name.Length <= MaxLength && ValidName().IsMatch(name);

    /// <summary>Turns an arbitrary folder name into a valid default name.</summary>
    public static string Sanitize(string raw)
    {
        var s = InvalidChars().Replace(raw.Trim(), "-").Trim('-');
        if (s.Length > MaxLength) s = s[..MaxLength];
        return s.Length == 0 ? "EXPERT" : s;
    }

    /// <summary><paramref name="baseName"/>, or <c>baseName-2</c>, <c>-3</c>… if already taken (case-insensitive).</summary>
    public static string Unique(string baseName, IEnumerable<string> taken)
    {
        var set = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
            if (!set.Contains($"{baseName}-{i}")) return $"{baseName}-{i}";
    }

    /// <summary>Colors are stored as <c>#RRGGBB</c> (the format Windows Terminal's --tabColor takes).</summary>
    public static bool IsHexColor([NotNullWhen(true)] string? value) => value is not null && HexColor().IsMatch(value);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ValidName();

    [GeneratedRegex("[^A-Za-z0-9_-]+")]
    private static partial Regex InvalidChars();
}
