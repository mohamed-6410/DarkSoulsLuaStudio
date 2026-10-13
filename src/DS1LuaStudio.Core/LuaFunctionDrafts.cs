using System.Text.Json;
using System.Text.RegularExpressions;

namespace DS1LuaStudio.Core;

public sealed record LuaFunctionSpan(string Name, int Start, int Length)
{
    public string Extract(string source) => source.Substring(Start, Length);
}

/// <summary>Finds complete function blocks without treating strings/comments as Lua keywords.</summary>
public static class LuaFunctionDrafts
{
    private static readonly HashSet<string> Keywords = new("and break do else elseif end false for function if in local nil not or repeat return then true until while".Split(' '));
    public sealed record Token(string Text, int Start, int End);
    private sealed record Block(string Kind, int Start, string? Name = null) { public bool WaitingForDo { get; set; } }

    public static IReadOnlyList<Token> Tokens(string source)
    {
        var result = new List<Token>();
        int i = 0;
        bool LongBracket(int start, out int equals, out int after)
        {
            equals = 0; after = start;
            if (start >= source.Length || source[start] != '[') return false;
            int p = start + 1;
            while (p < source.Length && source[p] == '=') { equals++; p++; }
            if (p >= source.Length || source[p] != '[') return false;
            after = p + 1; return true;
        }
        while (i < source.Length)
        {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            if (source[i] == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                if (LongBracket(i + 2, out int eq, out int after))
                { string close = "]" + new string('=', eq) + "]"; int end = source.IndexOf(close, after, StringComparison.Ordinal); i = end < 0 ? source.Length : end + close.Length; }
                else { while (i < source.Length && source[i] is not ('\r' or '\n')) i++; }
                continue;
            }
            if (LongBracket(i, out int level, out int body))
            { string close = "]" + new string('=', level) + "]"; int end = source.IndexOf(close, body, StringComparison.Ordinal); i = end < 0 ? source.Length : end + close.Length; continue; }
            if (source[i] is '\'' or '"')
            {
                char quote = source[i++];
                while (i < source.Length) { if (source[i] == '\\') { i = Math.Min(source.Length, i + 2); continue; } if (source[i++] == quote) break; }
                continue;
            }
            int start = i++;
            if (char.IsLetter(source[start]) || source[start] == '_')
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
            result.Add(new Token(source[start..i], start, i));
        }
        return result;
    }

    public static IReadOnlyList<LuaFunctionSpan> FindFunctions(string source)
    {
        var tokens = Tokens(source);
        var stack = new Stack<Block>();
        var functions = new List<LuaFunctionSpan>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Text)
            {
                case "function":
                    string? name = null;
                    int start = token.Start;
                    int p = i + 1;
                    if (p < tokens.Count && IsIdentifier(tokens[p].Text))
                    {
                        name = tokens[p++].Text;
                        while (p + 1 < tokens.Count && tokens[p].Text is "." or ":" && IsIdentifier(tokens[p + 1].Text))
                        { name += tokens[p].Text + tokens[p + 1].Text; p += 2; }
                        if (i > 0 && tokens[i - 1].Text == "local") start = tokens[i - 1].Start;
                    }
                    else if (i >= 2 && tokens[i - 1].Text == "=" && IsIdentifier(tokens[i - 2].Text))
                    {
                        int first = i - 2;
                        name = tokens[first].Text;
                        while (first >= 2 && tokens[first - 1].Text == "." && IsIdentifier(tokens[first - 2].Text))
                        { first -= 2; name = tokens[first].Text + "." + name; }
                        start = tokens[first].Start;
                        if (first > 0 && tokens[first - 1].Text == "local") start = tokens[first - 1].Start;
                    }
                    stack.Push(new Block("function", start, name));
                    break;
                case "if": case "repeat": stack.Push(new Block(token.Text, token.Start)); break;
                case "for": case "while": stack.Push(new Block(token.Text, token.Start) { WaitingForDo = true }); break;
                case "do":
                    if (stack.TryPeek(out var loop) && loop.WaitingForDo) loop.WaitingForDo = false;
                    else stack.Push(new Block("do", token.Start));
                    break;
                case "until":
                    if (stack.TryPeek(out var repeat) && repeat.Kind == "repeat") stack.Pop();
                    break;
                case "end":
                    if (!stack.TryPeek(out var block) || block.Kind == "repeat") break;
                    stack.Pop();
                    if (block.Kind == "function" && block.Name is not null)
                        functions.Add(new LuaFunctionSpan(block.Name, block.Start, token.End - block.Start));
                    break;
            }
        }
        return functions.OrderBy(f => f.Start).ToList();
    }

    public static LuaFunctionSpan? AtCaret(string source, int caret) => FindFunctions(source)
        .Where(f => caret >= f.Start && caret <= f.Start + f.Length).OrderBy(f => f.Length).FirstOrDefault();

    public static IReadOnlyList<string> ReferencedOuterLocals(string source, LuaFunctionSpan function)
    {
        var before = Tokens(source[..function.Start]);
        var locals = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < before.Count; i++)
        {
            if (before[i].Text != "local") continue;
            int p = i + 1;
            if (before[p].Text == "function") p++;
            while (p < before.Count && IsIdentifier(before[p].Text))
            {
                locals.Add(before[p].Text);
                if (p + 1 >= before.Count || before[p + 1].Text != ",") break;
                p += 2;
            }
        }
        return Tokens(function.Extract(source)).Select(t => t.Text).Where(locals.Contains).Distinct(StringComparer.Ordinal).OrderBy(s => s).ToList();
    }

    public static bool IsIdentifier(string name) => Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$") && !Keywords.Contains(name);

    public static string Rename(string source, string oldName, string newName)
    {
        if (!IsIdentifier(newName)) throw new ArgumentException("Use a Lua identifier for the new function name.");
        if (!IsIdentifier(oldName)) throw new ArgumentException("Member functions need their receiver; edit their name in the preview.");
        var tokens = Tokens(source);
        if (oldName != newName && tokens.Any(t => t.Text == newName)) throw new ArgumentException("That name already occurs in the draft. Choose another name to avoid capturing a local or helper.");
        var matches = tokens.Where((t, index) => t.Text == oldName && (index == 0 || tokens[index - 1].Text is not ("." or ":")))
            .OrderByDescending(t => t.Start);
        foreach (var token in matches) source = source.Remove(token.Start, token.End - token.Start).Insert(token.Start, newName);
        return source;
    }
}

public sealed record ScriptDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public GameMode GameMode { get; init; }
    public bool IsTemplate { get; init; }
    public required string Source { get; init; }
    public string Notes { get; init; } = "";
    public string Origin { get; init; } = "";
    public DateTime SavedUtc { get; init; } = DateTime.UtcNow;
    public string Caption => $"{(IsTemplate ? "Template · " : "")}[{GameMode}] {Name}";
}

/// <summary>User-owned draft files. A malformed draft does not hide the rest of the library.</summary>
public sealed class ScriptDraftStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string DirectoryPath => directory;
    public IReadOnlyList<ScriptDraft> Load()
    {
        var drafts = new List<ScriptDraft>();
        if (!Directory.Exists(directory)) return drafts;
        foreach (string path in Directory.GetFiles(directory, "*.json"))
        {
            try { var draft = JsonSerializer.Deserialize<ScriptDraft>(File.ReadAllText(path)); if (draft is not null && !string.IsNullOrWhiteSpace(draft.Name) && draft.Source is not null) drafts.Add(draft); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return drafts.OrderByDescending(d => d.SavedUtc).ToList();
    }
    public void Save(ScriptDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.Source)) throw new ArgumentException("Give the draft a name and some Lua source.");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, draft.Id + ".json");
        string temp = Path.Combine(directory, draft.Id + ".tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(draft, Options));
        File.Move(temp, path, overwrite: true);
    }
}
