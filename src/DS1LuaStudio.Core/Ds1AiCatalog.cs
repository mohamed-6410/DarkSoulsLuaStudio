namespace DS1LuaStudio.Core;

/// <summary>One AI goal from a <c>.luainfo</c> table.</summary>
public sealed record AiGoalInfo(int Id, string Name, bool BattleInterrupt, bool LogicInterrupt, string? LogicInterruptName)
{
    public string? SourcePath { get; init; }
    public LuabndArchive? Archive { get; init; }
    public string? EntryName { get; init; }
}

/// <summary>A decompiled AI callback resolved back to the goal it belongs to.</summary>
public sealed record Ds1FunctionInfo(
    string FunctionName,
    string GoalName,
    string Suffix,
    IReadOnlyList<string> ParameterNames,
    AiGoalInfo? Goal)
{
    /// <summary>True when the parameters were named rather than left as generated f1_arg0 style.</summary>
    public bool HasRealParameterNames => ParameterNames.Count > 0;
}

/// <summary>
/// Links Dark Souls AI callback functions to the goal tables that describe them.
/// <para>
/// The naming convention in the shipped scripts is exact: every callback is
/// <c>&lt;goalName&gt;_&lt;suffix&gt;</c>, where <c>goalName</c> is the <c>Name</c> column of a
/// <c>.luainfo</c> row. Across the stock PTDE scripts that gave 274 named functions, every one of
/// which resolved to a goal. Knowing the goal tells us what the callback is for; knowing the
/// callback's arity lets the generated argument names be replaced with the real ones.
/// </para>
/// </summary>
public sealed class Ds1AiCatalog
{
    /// <summary>
    /// Callback suffixes and the parameter names observed for them. The arity is checked against
    /// the chunk before renaming, so a mismatch simply leaves the generated names alone.
    /// </summary>
    private static readonly Dictionary<string, string[]> GoalCallbackParameters = new(StringComparer.Ordinal)
    {
        // Every *_Activate / _Update / _Terminate / _Interupt in the stock scripts takes (ai, goal).
        ["Activate"] = new[] { "ai", "goal" },
        ["Update"] = new[] { "ai", "goal" },
        ["Terminate"] = new[] { "ai", "goal" },
        ["Interupt"] = new[] { "ai", "goal" },
        ["Interrupt"] = new[] { "ai", "goal" },
        // *_Logic is the logic-interrupt entry point and only receives the ai handle.
        ["Logic"] = new[] { "ai" },
    };

    /// <summary>Action callbacks take the shared parameter table as a third argument.</summary>
    private static readonly string[] ActionParameters = { "ai", "goal", "paramTbl" };

    private readonly Dictionary<string, AiGoalInfo> _goalsByName;

    /// <summary>
    /// The same goals keyed by AI group ID and half. A battle script and its logic script share one
    /// ID, so the ID alone is not a key - the two rows have to be told apart.
    /// </summary>
    private readonly Dictionary<(int Id, bool Logic), AiGoalInfo> _goalsByGroup;

    /// <summary>
    /// Goal names with a trailing <c>_Battle</c> / <c>_Logic</c> removed. The act callbacks are
    /// named after the bare group, e.g. <c>BigHat_Logarn6030_Act01</c> belongs to the goal
    /// <c>BigHat_Logarn6030Battle</c>, so stripping the suffix is what lets those resolve too.
    /// </summary>
    private readonly Dictionary<string, string> _baseToGoalName;

    private Ds1AiCatalog(Dictionary<string, AiGoalInfo> goalsByName, IReadOnlyList<string> sources)
    {
        _goalsByName = goalsByName;
        Sources = sources;

        _goalsByGroup = new Dictionary<(int, bool), AiGoalInfo>();
        foreach (var goal in goalsByName.Values)
            _goalsByGroup.TryAdd((goal.Id, goal.LogicInterrupt), goal);

        _baseToGoalName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, goal) in goalsByName)
        {
            foreach (string candidate in BaseNamesFor(name))
                _baseToGoalName.TryAdd(candidate, name);
        }
    }

    /// <summary>
    /// The names an act callback can be written against. DS1 spells these inconsistently:
    /// battle goals end in <c>Battle</c> (<c>BigHat_Logarn6030Battle</c>) while logic goals end in
    /// <c>_Logic</c> (<c>HusibitoKojiki251000_Logic</c>), so both spellings are stripped.
    /// </summary>
    private static IEnumerable<string> BaseNamesFor(string goalName)
    {
        foreach (string suffix in new[] { "_Logic", "_Battle", "Logic", "Battle" })
        {
            if (goalName.Length > suffix.Length && goalName.EndsWith(suffix, StringComparison.Ordinal))
                yield return goalName[..^suffix.Length];
        }
    }

    /// <summary>Files the goals were read from.</summary>
    public IReadOnlyList<string> Sources { get; }

    public int GoalCount => _goalsByName.Count;

    public static Ds1AiCatalog Empty { get; } = new(new Dictionary<string, AiGoalInfo>(StringComparer.Ordinal), Array.Empty<string>());

    public static Ds1AiCatalog FromGoals(IEnumerable<AiGoalInfo> goals, IEnumerable<string>? sources = null)
    {
        var map = new Dictionary<string, AiGoalInfo>(StringComparer.Ordinal);

        foreach (var goal in goals)
        {
            if (string.IsNullOrEmpty(goal.Name)) continue;
            // First definition wins; the battle and logic rows can share an ID but never a name.
            map.TryAdd(goal.Name, goal);
        }

        return new Ds1AiCatalog(map, (sources ?? Array.Empty<string>()).ToList());
    }

    public static Ds1AiCatalog FromDocuments(IEnumerable<LuaInfoDocument> documents) =>
        FromGoals(
            documents.SelectMany(d => d.Goals.Select(g => new AiGoalInfo(
                g.Id, g.Name, g.BattleInterrupt, g.LogicInterrupt, g.LogicInterruptName)
                { SourcePath = d.FilePath, Archive = d.Archive, EntryName = d.EntryName })),
            documents.Select(d => d.FilePath ?? d.EntryName).Where(p => p is not null)!);

    public static Ds1AiCatalog FromArchive(LuabndArchive archive) => FromDocuments(
        archive.Entries.Where(e => e.LooksLikeLuaInfo).Select(e => LuaInfoDocument.FromArchive(archive, e.Name)));

    public static Ds1AiCatalog Combine(params Ds1AiCatalog[] catalogs) => FromGoals(
        catalogs.SelectMany(c => c._goalsByName.Values), catalogs.SelectMany(c => c.Sources).Distinct());

    /// <summary>
    /// Looks for goal tables near a script. Dark Souls PTDE keeps them two levels above the
    /// decompiled output, e.g. <c>...\m10_02_00_00-luabnd\m10_02_00_00.luainfo</c>.
    /// </summary>
    public static Ds1AiCatalog DiscoverNear(string scriptPath, int levelsUp = 4) =>
        DiscoverNearDirectory(Path.GetDirectoryName(Path.GetFullPath(scriptPath))!, levelsUp);

    /// <summary>
    /// The same search starting from a directory. Archive entries have no script file of their own,
    /// so their enclosing folder is the starting point instead.
    /// </summary>
    public static Ds1AiCatalog DiscoverNearDirectory(string directory, int levelsUp = 4, GameMode? mode = null)
    {
        var files = new List<string>();
        string? current = directory;

        for (int i = 0; i <= levelsUp && !string.IsNullOrEmpty(current); i++)
        {
            if (Directory.Exists(current))
                files.AddRange(Directory.GetFiles(current, "*.luainfo*"));

            current = Path.GetDirectoryName(current);
        }

        var documents = new List<LuaInfoDocument>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var doc = new LuaInfoDocument();
                doc.Load(file);
                if (mode.HasValue && doc.LongFormat != (mode == GameMode.DS3)) continue;
                documents.Add(doc);
            }
            catch
            {
                // A goal table we cannot read simply contributes nothing.
            }
        }

        return FromDocuments(documents);
    }

    public AiGoalInfo? FindGoal(string name) => _goalsByName.GetValueOrDefault(name);

    /// <summary>
    /// The battle and logic goals that share an AI group ID, in that order. Either half is null when
    /// the table has no row for it, which is normal: plenty of enemies only ship a battle script.
    /// </summary>
    public (AiGoalInfo? Battle, AiGoalInfo? Logic) FindPair(int id) =>
        (_goalsByGroup.GetValueOrDefault((id, false)), _goalsByGroup.GetValueOrDefault((id, true)));

    /// <summary>
    /// Resolves a decompiled function name to its goal and to real parameter names.
    /// Returns null when the name does not follow the <c>goal_suffix</c> convention.
    /// </summary>
    public Ds1FunctionInfo? Resolve(string? functionName, int parameterCount)
    {
        if (string.IsNullOrEmpty(functionName) || !functionName.Contains('_')) return null;

        // Exact goal names win (longest first), then the base names with _Battle/_Logic stripped.
        foreach (var (goalName, suffix) in MatchCandidates(functionName))
        {
            return new Ds1FunctionInfo(
                functionName,
                goalName,
                suffix,
                ParameterNamesFor(suffix, parameterCount),
                _goalsByName.GetValueOrDefault(goalName));
        }

        return null;

        IEnumerable<(string GoalName, string Suffix)> MatchCandidates(string name)
        {
            foreach (var goalName in _goalNamesByLength)
            {
                if (name.Length > goalName.Length + 1
                    && name[goalName.Length] == '_'
                    && name.StartsWith(goalName, StringComparison.Ordinal))
                {
                    yield return (goalName, name[(goalName.Length + 1)..]);
                    yield break;
                }
            }

            foreach (var baseName in _baseNamesByLength)
            {
                if (name.Length > baseName.Length + 1
                    && name[baseName.Length] == '_'
                    && name.StartsWith(baseName, StringComparison.Ordinal))
                {
                    yield return (_baseToGoalName[baseName], name[(baseName.Length + 1)..]);
                    yield break;
                }
            }
        }

        static string[] ParameterNamesFor(string suffixName, int arity) => arity switch
        {
            1 => suffixName is "Logic" ? new[] { "ai" } : Array.Empty<string>(),
            2 => GoalCallbackParameters.TryGetValue(suffixName, out var two) && two.Length == 2
                ? two
                : Array.Empty<string>(),
            // Three arguments only make sense for an act callback. A goal callback that happens to
            // take three arguments is a different signature we have not identified, so leave it.
            3 => GoalCallbackParameters.ContainsKey(suffixName) ? Array.Empty<string>() : ActionParameters,
            _ => Array.Empty<string>(),
        };
    }

    private IEnumerable<string> _goalNamesByLength =>
        _goalsByName.Keys.OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal);

    private IEnumerable<string> _baseNamesByLength =>
        _baseToGoalName.Keys.OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal);

    /// <summary>Goal names paired with the suffixes used by this title, for the editor palette.</summary>
    public IReadOnlyList<(AiGoalInfo Goal, string Suffix)> AllFunctions()
    {
        var result = new List<(AiGoalInfo, string)>();

        foreach (var goal in _goalsByName.Values.OrderBy(g => g.Id))
        {
            result.Add((goal, "Battle"));
            result.Add((goal, "Logic"));
            result.Add((goal, "Activate"));
            result.Add((goal, "Update"));
            result.Add((goal, "Terminate"));
            result.Add((goal, "Interupt"));
        }

        return result;
    }
}
