using System.Text.RegularExpressions;
using System.Xml.Linq;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.Tests;

public class LuaHighlightingDefinitionTests
{
    private static XDocument Parse() =>
        XDocument.Parse(LuaHighlightingDefinition.Build(AiNameDatabase.LoadEmbedded()));

    // The XSHD root declares a default namespace, so match on local names.
    private static IEnumerable<XElement> Colors(XDocument doc) =>
        doc.Root!.Elements().Where(e => e.Name.LocalName == "Color");

    [Fact]
    public void Build_ProducesWellFormedXml()
    {
        var doc = Parse();
        Assert.Equal("SyntaxDefinition", doc.Root?.Name.LocalName);
    }

    [Fact]
    public void Build_UsesTheXshdForegroundAttribute()
    {
        // XSHD rejects a "color" attribute on <Color>; it must be "foreground". Getting this wrong
        // throws while loading, which previously took the whole editor down with a stack overflow.
        foreach (var color in Colors(Parse()))
        {
            Assert.NotNull(color.Attribute("foreground"));
            Assert.Null(color.Attribute("color"));
        }
    }

    [Fact]
    public void Build_DeclaresEveryPaletteColour()
    {
        var declared = Colors(Parse()).Select(e => (string?)e.Attribute("name")).ToHashSet();
        foreach (var name in LuaHighlightingDefinition.Palette.Keys)
            Assert.Contains(name, declared);
    }

    [Fact]
    public void Build_ReferencesOnlyDeclaredColours()
    {
        var doc = Parse();
        var declared = Colors(doc).Select(e => (string?)e.Attribute("name")).ToHashSet();

        foreach (var used in doc.Descendants()
                     .Where(e => e.Attribute("color") is not null)
                     .Select(e => (string)e.Attribute("color")!)
                     .Distinct())
        {
            Assert.Contains(used, declared);
        }
    }

    [Fact]
    public void Build_IncludesLuaKeywords()
    {
        var words = Words(Parse());
        foreach (var keyword in new[] { "function", "local", "then", "return", "end", "nil" })
            Assert.Contains(keyword, words);
    }

    [Fact]
    public void Build_IncludesDs1AiVocabulary()
    {
        var words = Words(Parse());
        // Goal constants, engine calls and AI callbacks taken from VariableNamePatterns.json.
        Assert.Contains("GOAL_COMMON_Stay", words);
        Assert.Contains("GetDist", words);
        Assert.Contains("GlobalActivate", words);
        Assert.Contains("AI_DIR_TYPE_FR", words);
    }

    [Fact]
    public void Build_WithEmptyDatabase_StillProducesAValidDefinition()
    {
        var doc = XDocument.Parse(LuaHighlightingDefinition.Build(AiNameDatabase.Empty));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void Build_DoesNotEmitWordsWithInvalidCharacters()
    {
        var doc = XDocument.Parse(LuaHighlightingDefinition.Build(AiNameDatabase.Empty));
        foreach (var word in Words(doc))
        {
            Assert.All(word, c => Assert.True(char.IsLetterOrDigit(c) || c == '_',
                $"'{word}' contains a character that cannot appear in a keyword rule"));
        }
    }

    [Fact]
    public void Build_SpanPatternsAreValidRegularExpressions()
    {
        // XSHD begin/end attributes are regexes. An unescaped "[[" (Lua long string) is an
        // unterminated character class and makes the whole definition unloadable, so validate
        // every pattern here instead of discovering it at runtime.
        var doc = Parse();

        foreach (var element in doc.Descendants())
        {
            foreach (var attribute in new[] { "begin", "end" })
            {
                var pattern = element.Attribute(attribute)?.Value;
                if (pattern is null) continue;

                var result = Regex.Match(string.Empty, pattern);
                Assert.True(result.Success || pattern.Length > 0,
                    $"'{pattern}' on <{element.Name.LocalName}> is not a usable regex");
            }
        }
    }

    [Fact]
    public void Build_EscapesLuaLongStringBrackets()
    {
        var doc = Parse();
        var longString = doc.Descendants()
            .First(e => e.Attribute("begin")?.Value == @"\[\[");

        Assert.Equal(@"\]\]", longString.Attribute("end")?.Value);
    }

    [Fact]
    public void Build_DeclaresKeywordsAtRuleSetLevel()
    {
        // <Keywords> is only valid as a direct child of <RuleSet>; nesting it in a <Span>
        // causes the definition to be rejected.
        var doc = Parse();
        var ruleSet = doc.Root!.Descendants().First(e => e.Name.LocalName == "RuleSet");

        foreach (var keywords in ruleSet.Elements().Where(e => e.Name.LocalName == "Keywords"))
            Assert.NotNull(keywords.Attribute("color"));

        Assert.DoesNotContain(doc.Descendants().Where(e => e.Name.LocalName == "Span"),
            span => span.Elements().Any(e => e.Name.LocalName == "Keywords"));
    }

    private static HashSet<string> Words(XDocument doc) =>
        doc.Descendants().Where(e => e.Name.LocalName == "Word")
            .Select(e => e.Value)
            .ToHashSet();
}
