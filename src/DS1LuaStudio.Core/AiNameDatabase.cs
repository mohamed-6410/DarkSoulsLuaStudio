using System.Reflection;
using System.Text.Json;

namespace DS1LuaStudio.Core;

/// <summary>Describes how a Souls engine call maps onto readable argument/return names.</summary>
public sealed class AiCallPattern
{
    public IReadOnlyList<string> Return { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    /// <summary>Indexes into the engine call that get appended to the return value.</summary>
    public IReadOnlyList<int> ArgsToAppendToReturn { get; init; } = Array.Empty<int>();
}

/// <summary>
/// The DS1 Prepare to Die Edition AI naming database (<c>VariableNamePatterns.json</c>).
/// Drives editor highlighting, hover tooltips and the goal/function symbol palette.
/// </summary>
public sealed class AiNameDatabase
{
    public IReadOnlyDictionary<string, string> GlobalTranslations { get; private init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Function name -> parameter names, e.g. <c>GlobalUpdate -> ["ai", "goal", "dT"]</c>.</summary>
    public IReadOnlyDictionary<string, string[]> FunctionParameters { get; private init; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, AiCallPattern> Calls { get; private init; } =
        new Dictionary<string, AiCallPattern>(StringComparer.Ordinal);

    /// <summary>Goal constant -> the field names the goal record carries, e.g. <c>GOAL_COMMON_Stay</c>.</summary>
    public IReadOnlyDictionary<string, string[]> Goals { get; private init; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    public static AiNameDatabase Empty { get; } = new();

    /// <summary>Every identifier the database knows about, for highlighting.</summary>
    public IReadOnlySet<string> KnownSymbols { get; private init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Loads the database from the embedded copy shipped with the app.</summary>
    public static AiNameDatabase LoadEmbedded(GameMode mode = GameMode.DS1)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(mode == GameMode.DS3 ? "DS3Symbols.json" : "VariableNamePatterns.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return Empty;

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return Empty;
        return Load(stream);
    }

    public static AiNameDatabase Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public static AiNameDatabase Load(Stream stream)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var globals = ReadStringMap(root, "globalsTranslatedForAppending");
            var funcs = ReadStringArrayMap(root, "funcs");
            var goals = ReadStringArrayMap(root, "goals");

            var calls = new Dictionary<string, AiCallPattern>(StringComparer.Ordinal);
            if (root.TryGetProperty("calls", out var callsEl) && callsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in callsEl.EnumerateObject())
                {
                    calls[prop.Name] = new AiCallPattern
                    {
                        Return = ReadStringArray(prop.Value, "return"),
                        Args = ReadStringArray(prop.Value, "args"),
                        ArgsToAppendToReturn = ReadIntArray(prop.Value, "argsToAppendToReturn"),
                    };
                }
            }

            var symbols = new HashSet<string>(StringComparer.Ordinal);
            symbols.UnionWith(globals.Keys);
            symbols.UnionWith(funcs.Keys);
            symbols.UnionWith(calls.Keys);
            symbols.UnionWith(goals.Keys);
            if (root.TryGetProperty("symbols", out var extra))
                symbols.UnionWith(ReadStringArray(extra));

            return new AiNameDatabase
            {
                GlobalTranslations = globals,
                FunctionParameters = funcs,
                Calls = calls,
                Goals = goals,
                KnownSymbols = symbols,
            };
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string? DescribeFunction(string name) =>
        FunctionParameters.TryGetValue(name, out var p) && p.Length > 0
            ? $"function {name}({string.Join(", ", p)})"
            : null;

    public string? DescribeGoal(string name) =>
        Goals.TryGetValue(name, out var g) && g.Length > 0
            ? $"{name}({string.Join(", ", g.Select(f => f ?? "_"))})"
            : null;

    public string? DescribeCall(string name) =>
        Calls.TryGetValue(name, out var c)
            ? $"{name}({string.Join(", ", c.Args.Count > 0 ? c.Args : new[] { "..." })})" +
              (c.Return.Count > 0 ? $" -> {string.Join(", ", c.Return)}" : string.Empty)
            : null;

    /// <summary>Human readable description of any known symbol, or null.</summary>
    public string? Describe(string name) =>
        DescribeFunction(name) ?? DescribeGoal(name) ?? DescribeCall(name) ??
        (GlobalTranslations.TryGetValue(name, out var t) ? $"{name} = {t}" : KnownSymbols.Contains(name) ? name : null);

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string property)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
                map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : string.Empty;
        }
        return map;
    }

    private static Dictionary<string, string[]> ReadStringArrayMap(JsonElement root, string property)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
                map[p.Name] = ReadStringArray(p.Value);
        }
        return map;
    }

    private static string[] ReadStringArray(JsonElement el, string property = "")
    {
        if (!string.IsNullOrEmpty(property))
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(property, out el))
                return Array.Empty<string>();
        }
        if (el.ValueKind != JsonValueKind.Array) return Array.Empty<string>();

        return el.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty)
            .ToArray();
    }

    private static int[] ReadIntArray(JsonElement el, string property)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(property, out var arr))
            return Array.Empty<int>();
        if (arr.ValueKind != JsonValueKind.Array) return Array.Empty<int>();
        return arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number)
            .Select(e => e.GetInt32()).ToArray();
    }
}
