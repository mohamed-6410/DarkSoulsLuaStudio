using System.Text;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.Tests;

public class ScriptFileOpenerTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string Write(byte[] bytes, string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Classify_TreatsLuaBytecodeWithALuaExtensionAsBytecode()
    {
        // Dark Souls PTDE ships its AI scripts compiled, with a .lua extension. Trusting the
        // extension made the editor render raw bytecode as Shift-JIS text.
        var bytecode = LuaBytecodeService.Compile("local x = 1\nreturn x\n", LuaDialect.Lua50, Encoding.UTF8);
        string path = Write(bytecode, ".lua");

        Assert.Equal(ScriptFileKind.LuaBytecode, ScriptFileOpener.Classify(path));
    }

    [Fact]
    public void Classify_TreatsRealLuaSourceAsSource()
    {
        string path = Write(Encoding.UTF8.GetBytes("local x = 1\nreturn x\n"), ".lua");

        Assert.Equal(ScriptFileKind.LuaSource, ScriptFileOpener.Classify(path));
    }

    [Fact]
    public void Classify_TreatsHavokScriptBytecodeWithALuaExtensionAsBytecode()
    {
        // "\x1bLua", version 0x51, Havok Script marker 0x0E
        byte[] hks = { 0x1B, 0x4C, 0x75, 0x61, 0x51, 0x0E, 0x01, 0x04, 0x08, 0x04, 0x08, 0x00 };
        string path = Write(hks, ".lua");

        Assert.Equal(ScriptFileKind.LuaBytecode, ScriptFileOpener.Classify(path));
    }

    [Fact]
    public void Classify_DoesNotMistakeShiftJisTextForBytecode()
    {
        // Japanese text encoded as Shift-JIS; the leading bytes must not look like "\x1bLua".
        var sjis = LuaBytecodeService.GetShiftJis();
        string path = Write(sjis.GetBytes("-- 敵のAI\nlocal x = 1\n"), ".lua");

        Assert.Equal(ScriptFileKind.LuaSource, ScriptFileOpener.Classify(path));
    }

    [Fact]
    public void Classify_HandlesFileExtensions()
    {
        Assert.Equal(ScriptFileKind.LuaInfo, ScriptFileOpener.Classify("m10_02_00_00.luainfo"));
        Assert.Equal(ScriptFileKind.LuaGnl, ScriptFileOpener.Classify("a.luagnl"));
        Assert.Equal(ScriptFileKind.LuabndArchive, ScriptFileOpener.Classify("aistub.luabnd"));
    }

    [Fact]
    public void OpenBytecode_ProducesReadableSourceFromALuaExtensionFile()
    {
        var bytecode = LuaBytecodeService.Compile(
            "local function fight(ai, goal)\n    return goal:AddGoal(6080)\nend\n", LuaDialect.Lua50, Encoding.UTF8);

        string path = Write(bytecode, ".lua");
        var doc = ScriptFileOpener.OpenBytecode(path);

        Assert.Equal(LuaDialect.Lua50, doc.Dialect);
        Assert.Contains("function", doc.Text);
        Assert.Contains("AddGoal", doc.Text);
        Assert.False(doc.Text.Any(c => c == '\0'), "decompiled source must not contain NUL bytes");
    }

    [Fact]
    public void Detect_DoesNotTreatTruncatedFilesAsBytecode()
    {
        // Too short to carry the signature, so they stay ordinary (source) files.
        string tiny = Write(new byte[] { 0x1B, 0x4C }, ".lua");
        Assert.NotEqual(ScriptFileKind.LuaBytecode, ScriptFileOpener.Classify(tiny));

        string noVersion = Write(new byte[] { 0x1B, 0x4C, 0x75, 0x61 }, ".lua");
        Assert.NotEqual(ScriptFileKind.LuaBytecode, ScriptFileOpener.Classify(noVersion));

        string badVersion = Write(new byte[] { 0x1B, 0x4C, 0x75, 0x61, 0x99, 0x01, 0x02, 0x03 }, ".lua");
        Assert.NotEqual(ScriptFileKind.LuaBytecode, ScriptFileOpener.Classify(badVersion));
    }
}
