using System.Text;

namespace DS1LuaStudio.Core;

/// <summary>
/// Generates the XSHD syntax definition used by the code editor.
/// <para>
/// XSHD (the AvalonEdit XML format) declares colours as
/// <c>&lt;Color name="X" foreground="#RRGGBB" /&gt;</c>. Note the attribute is
/// <c>foreground</c> - writing <c>color=</c> is rejected by the parser.
/// </para>
/// Kept in the core library, away from any editor dependency, so the generated markup can be
/// unit tested.
/// </summary>
public static class LuaHighlightingDefinition
{
    private static readonly string[] Keywords =
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto",
        "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true",
        "until", "while",
    };

    /// <summary>Colours used by the generated definition, keyed by the XSHD colour name.</summary>
    public static IReadOnlyDictionary<string, string> Palette => new Dictionary<string, string>
    {
        ["Comment"] = "#8A93A2",
        ["String"] = "#B7D98A",
        ["Number"] = "#E0A340",
        ["Keyword"] = "#8FB8E8",
        ["Operator"] = "#C3CAD6",
        ["Func"] = "#D7B27A",
        ["Global"] = "#6FCF7F",
        ["Goal"] = "#D08CC7",
        ["Field"] = "#9FD4E0",
    };

    public static string Build(AiNameDatabase database)
    {
        database ??= AiNameDatabase.Empty;
        var palette = Palette;
        var sb = new StringBuilder(32 * 1024);

        sb.AppendLine("""<?xml version="1.0"?>""");
        sb.AppendLine("""<SyntaxDefinition name="DS1Lua" extensions=".lua" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">""");

        // XSHD declares colours with a "foreground" attribute; "color" is not permitted here.
        AppendColor(sb, "Comment", palette["Comment"], " fontStyle=\"italic\"");
        AppendColor(sb, "String", palette["String"]);
        AppendColor(sb, "Number", palette["Number"]);
        AppendColor(sb, "Keyword", palette["Keyword"]);
        AppendColor(sb, "Operator", palette["Operator"]);
        AppendColor(sb, "Func", palette["Func"]);
        AppendColor(sb, "Global", palette["Global"]);
        AppendColor(sb, "Goal", palette["Goal"], " fontWeight=\"bold\"");
        AppendColor(sb, "Field", palette["Field"]);

        sb.AppendLine("  <RuleSet>");
        sb.AppendLine("""    <Span color="Comment" begin="--" />""");
        // Lua long strings: the brackets have to be escaped, otherwise "[[" is an unterminated
        // regex character class and the whole definition fails to parse.
        sb.AppendLine("""    <Span color="String" begin="\[\[" end="\]\]" />""");
        sb.AppendLine("""    <Span color="String" begin="&quot;" end="&quot;" />""");
        sb.AppendLine("""    <Span color="String" begin="'" end="'" />""");
        sb.AppendLine("""    <Span color="Number" begin="\b0[xX][0-9a-fA-F]+\b|\b\d+(\.\d+)?([eE][+-]?\d+)?\b" />""");

        // Keywords are a RuleSet level element in XSHD, each carrying its own colour. They must
        // not be nested inside a <Span>.
        AppendKeywords(sb, "Keyword", Keywords, " fontWeight=\"bold\"");
        AppendKeywords(sb, "Goal", database.Goals.Keys);
        AppendKeywords(sb, "Field", database.Goals.Values.SelectMany(v => v));
        AppendKeywords(sb, "Func", database.FunctionParameters.Keys.Concat(database.Calls.Keys));
        AppendKeywords(sb, "Global", database.GlobalTranslations.Keys);

        // Brackets are deliberately left uncoloured so table indexing stays readable.
        sb.AppendLine("""    <Span color="Operator" begin="[-+*/%^#&lt;&gt;=~;,.:|]+" />""");
        sb.AppendLine("  </RuleSet>");
        sb.AppendLine("</SyntaxDefinition>");

        return sb.ToString();
    }

    private static void AppendColor(StringBuilder sb, string name, string hex, string extra = "")
    {
        sb.Append("  <Color name=\"").Append(name)
          .Append("\" foreground=\"").Append(hex).Append('"')
          .Append(extra)
          .AppendLine(" />");
    }

    private static void AppendKeywords(StringBuilder sb, string color, IEnumerable<string> words,
        string extra = "")
    {
        var list = words
            .Where(w => !string.IsNullOrEmpty(w) && w.All(c => char.IsLetterOrDigit(c) || c == '_'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToList();

        if (list.Count == 0) return;

        sb.Append("    <Keywords color=\"").Append(color).Append('"')
          .Append(extra).AppendLine(">");
        foreach (var w in list) sb.Append("      <Word>").Append(Escape(w)).AppendLine("</Word>");
        sb.AppendLine("    </Keywords>");
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
