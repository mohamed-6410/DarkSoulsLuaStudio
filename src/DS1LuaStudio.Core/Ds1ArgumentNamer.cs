using System.Text.RegularExpressions;

namespace DS1LuaStudio.Core;

/// <summary>A function as it appears in decompiled source, with its internal decompiler id.</summary>
public sealed record ParsedFunction(string Name, int FunctionId, int Arity);

/// <summary>
/// Rewrites the decompiler's generated argument names (<c>f1_arg0</c>) into the names Dark Souls AI
/// callbacks actually use, using the goal tables as the source of truth.
/// </summary>
public static class Ds1ArgumentNamer
{
    // A Lua 5.0 chunk does not store per-function names, so the decompiler recovers them (from the
    // globals the chunk assigns). The generated argument names still carry the function's internal
    // id, which is what lets the two be correlated here.
    private static readonly Regex Declaration = new(
        @"\bfunction\s+(?<name>[A-Za-z_][A-Za-z0-9_.:]*)\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex GeneratedArgument =
        new(@"\bf(?<fn>\d+)_arg(?<arg>\d+)\b", RegexOptions.Compiled);

    public sealed record Result(
        string Source,
        IReadOnlyList<ParsedFunction> Functions,
        IReadOnlyList<Ds1FunctionInfo> Resolved,
        int RenamedIdentifiers);

    /// <summary>
    /// Reads every function declaration out of decompiled Lua and pairs it with its function id.
    /// </summary>
    public static IReadOnlyList<ParsedFunction> ParseDeclarations(string source)
    {
        var result = new List<ParsedFunction>();
        if (string.IsNullOrEmpty(source)) return result;

        foreach (Match match in Declaration.Matches(source))
        {
            string name = match.Groups["name"].Value;
            string parameters = match.Groups["params"].Value.Trim();
            if (parameters.Length == 0) continue;

            // The first generated argument tells us which internal function this is.
            var first = GeneratedArgument.Match(parameters);
            if (!first.Success) continue;
            if (!int.TryParse(first.Groups["fn"].Value, out int functionId)) continue;

            int arity = parameters.Split(',').Length;
            result.Add(new ParsedFunction(name, functionId, arity));
        }

        return result;
    }

    /// <summary>
    /// Applies the renaming. Only parameters of callbacks that resolve to a goal with a known
    /// signature are touched; everything else is left exactly as decompiled.
    /// </summary>
    public static Result Apply(string source, Ds1AiCatalog catalog)
    {
        var declarations = ParseDeclarations(source);

        if (string.IsNullOrEmpty(source) || catalog.GoalCount == 0 || declarations.Count == 0)
            return new Result(source, declarations, Array.Empty<Ds1FunctionInfo>(), 0);

        // functionId -> the names its parameters should get.
        var namesByFunction = new Dictionary<int, string[]>();
        var resolved = new List<Ds1FunctionInfo>();

        foreach (var declaration in declarations)
        {
            var info = catalog.Resolve(declaration.Name, declaration.Arity);
            if (info is not { HasRealParameterNames: true }) continue;

            namesByFunction[declaration.FunctionId] = info.ParameterNames.ToArray();
            resolved.Add(info);
        }

        if (namesByFunction.Count == 0)
            return new Result(source, declarations, resolved, 0);

        // If a name we would introduce already exists as an identifier, renaming would shadow it.
        // Leave the generated names alone rather than risk changing behaviour.
        foreach (var name in namesByFunction.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal))
        {
            if (GeneratedArgument.IsMatch(source) && Regex.IsMatch(source, $@"\b{Regex.Escape(name)}\b"))
            {
                return new Result(source, declarations, resolved, 0);
            }
        }

        int renamed = 0;
        var rewritten = GeneratedArgument.Replace(source, match =>
        {
            if (!int.TryParse(match.Groups["fn"].Value, out int functionId)) return match.Value;
            if (!int.TryParse(match.Groups["arg"].Value, out int argIndex)) return match.Value;
            if (!namesByFunction.TryGetValue(functionId, out var names)) return match.Value;
            if (argIndex >= names.Length) return match.Value;

            renamed++;
            return names[argIndex];
        });

        return new Result(rewritten, declarations, resolved, renamed);
    }
}
