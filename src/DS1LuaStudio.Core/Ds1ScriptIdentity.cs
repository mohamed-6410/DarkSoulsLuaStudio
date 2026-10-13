namespace DS1LuaStudio.Core;

/// <summary>Which half of an AI group a script implements.</summary>
public enum AiScriptKind
{
    Unknown,
    Battle,
    Logic,
}

/// <summary>
/// The identity carried by a Dark Souls AI script's file name. <c>251000_battle.lua</c> and
/// <c>251000_logic.lua</c> are the two halves of AI group 251000, and that ID is the one the
/// <c>.luainfo</c> rows use.
/// </summary>
public sealed record Ds1ScriptIdentity(int GoalId, AiScriptKind Kind)
{
    public string Label => Kind == AiScriptKind.Logic ? "Logic" : "Battle";

    /// <summary>
    /// Reads the identity from a script path, or from a bare entry name as used inside a
    /// <c>.luabnd</c>. Returns null for anything not following the
    /// <c>&lt;id&gt;_battle|logic</c> convention.
    /// </summary>
    public static Ds1ScriptIdentity? TryParse(string? pathOrEntryName)
    {
        if (string.IsNullOrEmpty(pathOrEntryName)) return null;

        string name = Path.GetFileNameWithoutExtension(pathOrEntryName);
        int underscore = name.LastIndexOf('_');

        // Needs both an ID and a half, so a leading or trailing underscore is out.
        if (underscore <= 0 || underscore == name.Length - 1) return null;

        if (!int.TryParse(name[..underscore], out int goalId)) return null;

        AiScriptKind kind = name[(underscore + 1)..].ToLowerInvariant() switch
        {
            "battle" => AiScriptKind.Battle,
            "logic" => AiScriptKind.Logic,
            _ => AiScriptKind.Unknown,
        };

        return kind == AiScriptKind.Unknown ? null : new Ds1ScriptIdentity(goalId, kind);
    }
}

/// <summary>
/// The link shown against an open battle or logic script: which row of the goal table that script
/// belongs to, and whether the table actually has it.
/// </summary>
public sealed record ScriptGoalLink(Ds1ScriptIdentity Identity, string Name)
{
    /// <summary>True when the goal table has a row for this script's half of the group.</summary>
    public bool Exists => Name.Length > 0;

    public int GoalId => Identity.GoalId;

    /// <summary>"Battle 251000" - the half of the group this script implements.</summary>
    public string Caption => $"{Identity.Label} {GoalId}";

    /// <summary>Shown on the link itself; falls back to the caption when the row is missing.</summary>
    public string Display => Exists ? Name : Caption;

    public string ToolTip => Exists
        ? $"Open {Caption}: {Name}"
        : $"{Caption} has no row in the goal table next to this script.";

    /// <summary>
    /// Resolves the link. Only the half the script implements is considered: a
    /// <c>251000_battle.lua</c> is the battle row and never the logic one.
    /// </summary>
    public static ScriptGoalLink From(Ds1ScriptIdentity identity, (AiGoalInfo? Battle, AiGoalInfo? Logic) pair)
    {
        var goal = identity.Kind == AiScriptKind.Logic ? pair.Logic : pair.Battle;
        return new ScriptGoalLink(identity, goal?.Name ?? string.Empty);
    }
}