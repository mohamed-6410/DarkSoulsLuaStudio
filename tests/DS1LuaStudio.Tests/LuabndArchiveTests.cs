using System.Text;
using DS1LuaStudio.Core;
using SoulsFormats;

namespace DS1LuaStudio.Tests;

public class LuabndArchiveTests
{
    private static byte[] Script(string source) =>
        LuaBytecodeService.Compile(source, LuaDialect.Lua50, Encoding.UTF8);

    private static string WriteTempBnd3(string name, params (string Name, byte[] Bytes)[] entries)
    {
        var bnd = new BND3();
        foreach (var (entryName, bytes) in entries)
            bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, entryName, bytes));

        var path = Path.Combine(Path.GetTempPath(), name);
        bnd.Write(path);
        return path;
    }

    private const string Tiny = "local x = 1\nreturn x\n";

    [Fact]
    public void TryOpen_ReadsBnd3()
    {
        var path = WriteTempBnd3($"{Guid.NewGuid():N}.luabnd",
            ("aistub_0", Script(Tiny)),
            ("notes", new byte[] { 1, 2, 3 }));

        try
        {
            var archive = LuabndArchive.TryOpen(path);
            Assert.NotNull(archive);
            Assert.True(archive!.IsBnd3);

            var entries = archive.Entries;
            Assert.Equal(2, entries.Count);

            var script = entries.Single(e => e.Name == "aistub_0");
            Assert.True(script.LooksLikeLua);
            Assert.False(script.LooksLikeLuaInfo);
            Assert.False(entries.Single(e => e.Name == "notes").LooksLikeLua);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryOpen_MarksLuainfoEntries()
    {
        // LUAINFO files carry the "LUAI" magic, which is how entries are recognised inside a binder.
        var goalInfo = new LUAINFO(false, false);
        goalInfo.Goals.Add(new LUAINFO.Goal(351000, "BattleBigCrow", true, false));
        var infoPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luainfo");
        goalInfo.Write(infoPath);

        try
        {
            var bndPath = WriteTempBnd3($"{Guid.NewGuid():N}.luabnd",
                ("m10_02_00_00.luainfo", File.ReadAllBytes(infoPath)));

            try
            {
                var archive = LuabndArchive.TryOpen(bndPath)!;
                var entry = archive.Entries.Single();
                Assert.True(entry.LooksLikeLuaInfo);
                Assert.False(entry.LooksLikeLua);
            }
            finally
            {
                File.Delete(bndPath);
            }
        }
        finally
        {
            File.Delete(infoPath);
        }
    }

    [Fact]
    public void TryOpen_ReturnsNullForNonBinder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            Assert.Null(LuabndArchive.TryOpen(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplaceEntry_ThenSave_PersistsNewBytes()
    {
        var path = WriteTempBnd3($"{Guid.NewGuid():N}.luabnd", ("aistub_0", Script(Tiny)));
        var outPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luabnd");

        try
        {
            var archive = LuabndArchive.TryOpen(path)!;
            var newBytes = Script("local changed = true\nreturn changed\n");
            archive.ReplaceEntry("aistub_0", newBytes);
            Assert.True(archive.IsDirty);

            archive.Save(outPath);

            var reopened = LuabndArchive.TryOpen(outPath)!;
            Assert.Equal(newBytes, reopened.GetBytes("aistub_0"));
            Assert.False(reopened.IsDirty);
        }
        finally
        {
            File.Delete(path);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void ReplaceEntry_UnknownNameThrows()
    {
        var path = WriteTempBnd3($"{Guid.NewGuid():N}.luabnd", ("aistub_0", Script(Tiny)));
        try
        {
            var archive = LuabndArchive.TryOpen(path)!;
            Assert.Throws<ArgumentException>(() => archive.ReplaceEntry("nope", new byte[] { 1 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ArchiveEntry_EditCompilesAndWritesBack()
    {
        var path = WriteTempBnd3($"{Guid.NewGuid():N}.luabnd", ("aistub_0", Script(Tiny)));

        try
        {
            var archive = LuabndArchive.TryOpen(path)!;
            var doc = ScriptFileOpener.OpenArchiveEntry(archive, "aistub_0");

            Assert.True(doc.IsFromArchive);
            Assert.Equal("aistub_0", doc.EntryName);
            Assert.Contains(Path.GetFileName(path), doc.Origin);
            Assert.Contains("aistub_0", doc.Origin);

            doc.Text = "local edited = 42\nreturn edited\n";
            doc.SaveCompiledToArchive();

            var reopened = LuabndArchive.TryOpen(path)!;
            var roundTripped = LuaScriptDocument.FromBytecode(reopened.GetBytes("aistub_0")!, "aistub_0");
            Assert.Contains("edited", roundTripped.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractAllScripts_WritesOneFilePerScript()
    {
        var path = WriteTempBnd3($"{Guid.NewGuid():N}.luabnd",
            ("aistub_0", Script(Tiny)),
            ("aistub_1", Script("local y = 2\n")),
            ("readme.txt", new byte[] { 9, 9 }));

        var outDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");

        try
        {
            var archive = LuabndArchive.TryOpen(path)!;
            var written = ScriptFileOpener.ExtractAllScripts(archive, outDir);

            Assert.Equal(2, written.Count);
            Assert.All(written, f => Assert.True(File.Exists(f)));
            Assert.All(written, f => Assert.EndsWith(".lua", f));

            var contents = written.Select(File.ReadAllText).ToList();
            Assert.Contains(contents, c => c.Contains("return x"));
            Assert.Contains(contents, c => c.Contains("local y = 2"));
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public void Classify_DetectsKindsByExtension()
    {
        Assert.Equal(ScriptFileKind.LuaInfo, ScriptFileOpener.Classify("m10_02_00_00.luainfo"));
        Assert.Equal(ScriptFileKind.LuaGnl, ScriptFileOpener.Classify("a.luagnl"));
        Assert.Equal(ScriptFileKind.LuaSource, ScriptFileOpener.Classify("script.lua"));
        Assert.Equal(ScriptFileKind.LuabndArchive, ScriptFileOpener.Classify("aistub.luabnd"));
    }

    [Fact]
    public void LuaGnl_RoundTrips()
    {
        var gnl = new LUAGNL(false, false);
        gnl.Globals.Add("ai");
        gnl.Globals.Add("goal");
        gnl.Globals.Add("self");

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luagnl");
        try
        {
            gnl.Write(path);

            var doc = new LuaGnlDocument();
            doc.Load(path);

            Assert.Equal(new[] { "ai", "goal", "self" }, doc.Globals);
            doc.Globals.Add("extra");
            doc.Save(path);

            var reread = new LuaGnlDocument();
            reread.Load(path);
            Assert.Equal(4, reread.Globals.Count);
            Assert.Equal("extra", reread.Globals[3]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
