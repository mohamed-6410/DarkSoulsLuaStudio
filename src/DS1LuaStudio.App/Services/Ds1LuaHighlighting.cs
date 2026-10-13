using System.IO;
using System.Xml;
using DS1LuaStudio.Core;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace DS1LuaStudio.App.Services;

/// <summary>
/// Loads the DS1-flavoured Lua syntax definition produced by
/// <see cref="LuaHighlightingDefinition"/>. A malformed definition must never stop the editor
/// from opening, so loading failures degrade to no highlighting rather than throwing.
/// </summary>
public static class Ds1LuaHighlighting
{
    private static IHighlightingDefinition? _cached;
    private static string? _failure;
    private static AiNameDatabase? _database;

    public static IHighlightingDefinition? Instance(AiNameDatabase database)
    {
        if (!ReferenceEquals(_database, database)) { _cached = null; _failure = null; _database = database; }
        if (_cached is not null) return _cached;
        if (_failure is not null) return null;

        try
        {
            string xshd = LuaHighlightingDefinition.Build(database);
            using var stringReader = new StringReader(xshd);
            using var xmlReader = XmlReader.Create(stringReader);
            _cached = HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
            return _cached;
        }
        catch (Exception ex)
        {
            // Without highlighting the script is still perfectly editable.
            _failure = ex.Message;
            Log.Warn($"Syntax highlighting could not be loaded: {ex.Message}");
            return null;
        }
    }
}
