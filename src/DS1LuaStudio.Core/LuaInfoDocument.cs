using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SoulsFormats;

namespace DS1LuaStudio.Core;

public enum LuaInfoIssueSeverity { Info, Warning, Error }

public sealed record LuaInfoIssue(LuaInfoIssueSeverity Severity, int RowIndex, string Message)
{
    public override string ToString() => RowIndex >= 0
        ? $"Row {RowIndex + 1}: {Message}"
        : Message;
}

/// <summary>An editable view over a single <see cref="LUAINFO.Goal"/>.</summary>
public sealed class GoalRow : INotifyPropertyChanged
{
    private readonly LUAINFO.Goal _goal;

    public GoalRow(LUAINFO.Goal goal, int index)
    {
        _goal = goal;
        Index = index;
    }

    /// <summary>Position in the file. Shown in the grid; not stored in the binary.</summary>
    public int Index { get; internal set; }

    internal LUAINFO.Goal Goal => _goal;

    public int Id
    {
        get => _goal.ID;
        set { if (_goal.ID != value) { _goal.ID = value; Raise(); } }
    }

    public string Name
    {
        get => _goal.Name ?? string.Empty;
        set { var v = value ?? string.Empty; if (Name != v) { _goal.Name = v; Raise(); } }
    }

    public bool BattleInterrupt
    {
        get => _goal.BattleInterrupt;
        set { if (_goal.BattleInterrupt != value) { _goal.BattleInterrupt = value; Raise(); } }
    }

    public bool LogicInterrupt
    {
        get => _goal.LogicInterrupt;
        set { if (_goal.LogicInterrupt != value) { _goal.LogicInterrupt = value; Raise(); } }
    }

    public string? LogicInterruptName
    {
        get => _goal.LogicInterruptName;
        set { if (_goal.LogicInterruptName != value) { _goal.LogicInterruptName = string.IsNullOrEmpty(value) ? null : value; Raise(); } }
    }

    /// <summary>True when the row carries no interrupt function name at all.</summary>
    public bool HasLogicName => !string.IsNullOrEmpty(LogicInterruptName);

    public GoalRow Clone() => new(
        new LUAINFO.Goal(Id, Name, BattleInterrupt, LogicInterrupt, LogicInterruptName), Index);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// An editable Dark Souls AI goal table (<c>*.luainfo</c>).
/// Keeps the original endianness/width so saves are byte-faithful.
/// </summary>
public sealed class LuaInfoDocument
{
    private LUAINFO? _source;
    private SoulsFormats.DCX.CompressionInfo _compression = new SoulsFormats.DCX.NoCompressionInfo();

    public ObservableCollection<GoalRow> Goals { get; } = new();

    public string? FilePath { get; private set; }

    public LuabndArchive? Archive { get; private set; }
    public string? EntryName { get; private set; }

    public static LuaInfoDocument FromArchive(LuabndArchive archive, string entryName)
    {
        var doc = new LuaInfoDocument();
        using var stream = new MemoryStream(archive.GetBytes(entryName) ?? throw new ArgumentException("Missing archive entry."));
        doc.Load(stream);
        doc.Archive = archive;
        doc.EntryName = entryName;
        return doc;
    }

    public void SaveToArchive()
    {
        if (Archive is null || EntryName is null) throw new InvalidOperationException("No archive origin.");
        Archive.ReplaceEntry(EntryName, ToBytes());
        Archive.Save();
        TakeSnapshot();
        IsDirty = false;
    }

    public bool BigEndian { get; private set; }

    public bool LongFormat { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>Rows as they were when last loaded or saved, used to detect edits.</summary>
    private readonly List<GoalRow> _snapshot = new();

    public bool IsEmpty => Goals.Count == 0;

    public int LogicInterruptCount => Goals.Count(g => g.LogicInterruptName is { Length: > 0 });

    public void Load(string path)
    {
        using var stream = File.OpenRead(path);
        Load(stream, path);
    }

    public void Load(Stream stream, string? path = null)
    {
        LuaBytecodeService.EnsureEncodings();
        stream.Position = 0;

        var info = SoulsFile<LUAINFO>.Read(stream);
        _source = info;
        _compression = info.Compression;

        BigEndian = info.BigEndian;
        LongFormat = info.LongFormat;
        FilePath = path;

        Goals.Clear();
        foreach (var goal in info.Goals)
            Goals.Add(new GoalRow(goal, Goals.Count));

        Reindex();
        TakeSnapshot();
        IsDirty = false;
    }

    /// <summary>Starts a brand new, empty DS1-format goal table.</summary>
    public static LuaInfoDocument CreateNew(GameMode mode = GameMode.DS1)
    {
        var doc = new LuaInfoDocument
        {
            _source = new LUAINFO(bigEndian: false, longFormat: mode == GameMode.DS3),
            BigEndian = false,
            LongFormat = mode == GameMode.DS3,
        };
        doc.Goals.Add(new GoalRow(new LUAINFO.Goal(0, string.Empty, false, false), 0));
        doc.Reindex();
        doc.IsDirty = true;
        return doc;
    }

    public GoalRow AddGoal()
    {
        var row = new GoalRow(new LUAINFO.Goal(NextId(), string.Empty, false, false), Goals.Count);
        Goals.Add(row);
        Reindex();
        MarkDirty();
        return row;
    }

    /// <summary>
    /// Adds the battle + logic goal pair that one AI group needs. DS1 runs a battle script and a
    /// logic script together for the same enemy, so both goals must carry the same ID: the ID is
    /// the AI battle group, and the two goals are its behaviour and its interrupt behaviour.
    /// Every shared ID in the stock tables is exactly this shape.
    /// </summary>
    public (GoalRow Battle, GoalRow Logic) AddGoalPair() => AddGoalPair(NextId());

    /// <summary>
    /// Adds the battle + logic goal pair that one AI group needs, on a specific ID. Both rows go in
    /// next to each other because the game reads them as one group.
    /// </summary>
    public (GoalRow Battle, GoalRow Logic) AddGoalPair(int id)
    {
        var battle = new GoalRow(new LUAINFO.Goal(id, string.Empty, true, false), Goals.Count);
        Goals.Add(battle);

        var logic = new GoalRow(new LUAINFO.Goal(id, string.Empty, false, true), Goals.Count);
        Goals.Add(logic);

        Reindex();
        MarkDirty();
        return (battle, logic);
    }

    /// <summary>
    /// The battle and logic rows sharing an AI group ID, in that order. Either half is null when the
    /// table has no row for it, which is normal: plenty of enemies only ship a battle script.
    /// </summary>
    public (GoalRow? Battle, GoalRow? Logic) FindPair(int id)
    {
        GoalRow? battle = null;
        GoalRow? logic = null;

        foreach (var row in Goals)
        {
            if (row.Id != id) continue;
            if (row.BattleInterrupt) battle ??= row;
            if (row.LogicInterrupt) logic ??= row;
        }

        return (battle, logic);
    }

    public void AddGoal(GoalRow row)
    {
        row.Index = Goals.Count;
        Goals.Add(row);
        Reindex();
        MarkDirty();
    }

    public bool RemoveGoal(GoalRow row)
    {
        bool removed = Goals.Remove(row);
        if (removed)
        {
            Reindex();
            MarkDirty();
        }
        return removed;
    }

    public void Duplicate(GoalRow row)
    {
        var clone = row.Clone();
        clone.Id = NextId();
        clone.Name = string.IsNullOrEmpty(clone.Name) ? string.Empty : clone.Name + "_Copy";
        Goals.Insert(Math.Min(row.Index + 1, Goals.Count), clone);
        Reindex();
        MarkDirty();
    }

    public void Move(GoalRow row, int delta)
    {
        int target = Math.Clamp(row.Index + delta, 0, Goals.Count - 1);
        if (target == row.Index) return;
        Goals.Move(row.Index, target);
        Reindex();
        MarkDirty();
    }

    public void SortById()
    {
        var sorted = Goals.OrderBy(g => g.Id).ToList();
        Goals.Clear();
        foreach (var g in sorted) Goals.Add(g);
        Reindex();
        MarkDirty();
    }

    /// <summary>The smallest non-negative integer not already used as a goal ID.</summary>
    public int NextId()
    {
        var used = new HashSet<int>(Goals.Select(g => g.Id));
        for (int i = 0; ; i++)
            if (!used.Contains(i)) return i;
    }

    public void Reindex()
    {
        for (int i = 0; i < Goals.Count; i++)
            Goals[i].Index = i;
    }

    public void MarkDirty() => IsDirty = true;

    /// <summary>
    /// <summary>
    /// Re-checks the table for mistakes that break DS1 at runtime: names must be unique, a shared
    /// goal ID must be a proper battle+logic pair, and a row claiming to be a logic interrupt needs
    /// a function name.
    /// </summary>
    public IReadOnlyList<LuaInfoIssue> Validate()
    {
        var issues = new List<LuaInfoIssue>();

        if (Goals.Count == 0)
            issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Error, -1,
                "This file has no goals. FromSoftware's LUAINFO reader cannot detect the format of a goal-less file."));

        var dupNames = Goals.Where(g => g.Name.Length > 0)
            .GroupBy(g => g.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        // A goal ID is the AI battle group ID, and DS1 legitimately shares it: when a group needs
        // interrupt behaviour it is split into one battle goal and one logic goal under the same
        // ID. Verified across the stock tables (aiCommon, m10_02_00_00, m14_01_00_00): every
        // repeated ID is exactly one Battle + one Logic goal, and names are always unique.
        // Only a repeat that is NOT a clean battle+logic pair indicates a real mistake.
        var sharedIds = Goals
            .Select((GoalRow row, int index) => (row, index))
            .GroupBy(x => x.row.Id)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (id, members) in sharedIds)
        {
            int battleCount = members.Count(m => m.row.BattleInterrupt);
            int logicCount = members.Count(m => m.row.LogicInterrupt);
            bool isBattleLogicPair = members.Count == 2 && battleCount == 1 && logicCount == 1;

            foreach (var (row, index) in members)
            {
                if (isBattleLogicPair)
                {
                    var partner = members.First(m => m.index != index).row;
                    issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Info, index,
                        $"Goal ID {id} is shared with '{partner.Name}'. " +
                        "That is the normal battle + logic pair for one AI group."));
                }
                else
                {
                    issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Error, index,
                        $"Goal ID {id} is used by {members.Count} goals " +
                        $"({battleCount} battle, {logicCount} logic). A shared ID is only valid " +
                        "as exactly one battle goal plus one logic goal."));
                }
            }
        }

        for (int i = 0; i < Goals.Count; i++)
        {
            var g = Goals[i];
            if (g.Id < 0)
                issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Warning, i, $"Goal ID {g.Id} is negative."));

            if (string.IsNullOrWhiteSpace(g.Name))
                issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Warning, i, "Goal name is empty."));
            else if (dupNames.Contains(g.Name))
                issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Error, i,
                    $"Goal name '{g.Name}' is used more than once. Names identify goals and must be unique."));

            if (g.LogicInterrupt && string.IsNullOrEmpty(g.LogicInterruptName))
                issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Warning, i,
                    "Logic interrupt is ticked but no interrupt function name is set."));
            if (!g.LogicInterrupt && !string.IsNullOrEmpty(g.LogicInterruptName))
                issues.Add(new LuaInfoIssue(LuaInfoIssueSeverity.Info, i,
                    $"Interrupt name '{g.LogicInterruptName}' is set but the logic interrupt flag is not ticked."));
        }

        return issues;
    }

    /// <summary>Builds the binary representation exactly as <see cref="LUAINFO"/> writes it.</summary>
    public byte[] ToBytes()
    {
        var info = BuildSource();
        var path = Path.Combine(Path.GetTempPath(), $"luastudio-{Guid.NewGuid():N}.luainfo");
        try
        {
            info.Write(path);
            return File.ReadAllBytes(path);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    public void Save(string path)
    {
        var info = BuildSource();
        info.Write(path);
        FilePath = path;
        Archive = null;
        EntryName = null;
        TakeSnapshot();
        IsDirty = false;
    }

    private LUAINFO BuildSource()
    {
        // Preserve the flags exactly as they were read so a re-save is byte faithful.
        var info = new LUAINFO(BigEndian, LongFormat)
        {
            Compression = _compression,
        };
        foreach (var row in Goals)
        {
            info.Goals.Add(new LUAINFO.Goal(row.Id, row.Name, row.BattleInterrupt, row.LogicInterrupt, row.LogicInterruptName));
        }
        return info;
    }

    private void TakeSnapshot()
    {
        _snapshot.Clear();
        foreach (var g in Goals) _snapshot.Add(g.Clone());
    }

    /// <summary>Recomputes <see cref="IsDirty"/> by comparing against the last saved state.</summary>
    public void RefreshDirty()
    {
        if (Goals.Count != _snapshot.Count)
        {
            IsDirty = true;
            return;
        }
        for (int i = 0; i < Goals.Count; i++)
        {
            var a = Goals[i];
            var b = _snapshot[i];
            if (a.Id != b.Id || a.Name != b.Name ||
                a.BattleInterrupt != b.BattleInterrupt || a.LogicInterrupt != b.LogicInterrupt ||
                a.LogicInterruptName != b.LogicInterruptName)
            {
                IsDirty = true;
                return;
            }
        }
        IsDirty = false;
    }

    // ---- text interchange -------------------------------------------------

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ID,Name,BattleInterrupt,LogicInterrupt,LogicInterruptName");
        foreach (var g in Goals)
        {
            sb.AppendLine(string.Join(",",
                Csv(g.Id.ToString(CultureInfo.InvariantCulture)),
                Csv(g.Name),
                g.BattleInterrupt ? "true" : "false",
                g.LogicInterrupt ? "true" : "false",
                Csv(g.LogicInterruptName ?? string.Empty)));
        }
        return sb.ToString();
    }

    public static LuaInfoDocument FromCsv(string csv)
    {
        var doc = CreateNew();
        doc.Goals.Clear();

        var lines = SplitCsvLines(csv);
        for (int i = 0; i < lines.Count; i++)
        {
            var cells = SplitCsvLine(lines[i]);
            if (cells.Count == 0) continue;
            // skip the header row when present
            if (i == 0 && cells[0].Equals("ID", StringComparison.OrdinalIgnoreCase)) continue;

            string Cell(int n) => n < cells.Count ? cells[n] : string.Empty;
            if (!int.TryParse(Cell(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;

            var boolCell = new Func<string, bool>(s =>
                s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                s == "1");

            doc.Goals.Add(new GoalRow(
                new LUAINFO.Goal(id, Cell(1), boolCell(Cell(2)), boolCell(Cell(3)),
                    string.IsNullOrEmpty(Cell(4)) ? null : Cell(4)),
                doc.Goals.Count));
        }

        doc.Reindex();
        doc.MarkDirty();
        return doc;
    }

    public string ToJson() => JsonSerializer.Serialize(Goals.Select(g => new
    {
        id = g.Id,
        name = g.Name,
        battleInterrupt = g.BattleInterrupt,
        logicInterrupt = g.LogicInterrupt,
        logicInterruptName = g.LogicInterruptName,
    }), new JsonSerializerOptions { WriteIndented = true });

    public static LuaInfoDocument FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = CreateNew();
        result.Goals.Clear();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string? Str(string p) => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool Bool(string p) => el.TryGetProperty(p, out var v) &&
                                   (v.ValueKind == JsonValueKind.True ||
                                    (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

            result.Goals.Add(new GoalRow(
                new LUAINFO.Goal(
                    el.TryGetProperty("id", out var id) && id.TryGetInt32(out int iv) ? iv : 0,
                    Str("name") ?? string.Empty,
                    Bool("battleInterrupt"),
                    Bool("logicInterrupt"),
                    Str("logicInterruptName")),
                result.Goals.Count));
        }

        result.Reindex();
        result.MarkDirty();
        return result;
    }

    private static List<string> SplitCsvLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Where(l => l.Trim().Length > 0)
            .ToList();

    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
