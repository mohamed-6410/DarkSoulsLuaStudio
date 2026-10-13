using DS1LuaStudio.Core;
using SoulsFormats;

namespace DS1LuaStudio.Tests;

public class Ds3FormatTests
{
    [Fact]
    public void Ds3GoalTableUsesLongOffsetsAndUnicode()
    {
        var doc = LuaInfoDocument.CreateNew(GameMode.DS3);
        doc.Goals[0].Name = "Knight_騎士_Battle";
        doc.Goals[0].BattleInterrupt = true;
        var bytes = doc.ToBytes();
        var loaded = new LuaInfoDocument();
        loaded.Load(new MemoryStream(bytes));
        Assert.True(loaded.LongFormat);
        Assert.Equal(doc.Goals[0].Name, loaded.Goals[0].Name);
        Assert.Equal(bytes, loaded.ToBytes());
    }

    [Fact]
    public void BinderTablesSaveBackWithoutChangingOtherEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".luabnd.dcx");
        try
        {
            var info = LuaInfoDocument.CreateNew(GameMode.DS3);
            info.Goals[0].Id = 100000;
            info.Goals[0].Name = "KnightBattle";
            info.Goals[0].BattleInterrupt = true;
            var gnl = LuaGnlDocument.CreateNew(GameMode.DS3);
            gnl.Globals.Add("KnightBattle_Activate");
            var script = LuaBytecodeService.Compile("function KnightBattle_Activate(ai, goal) return ai:GetDist(0) end", LuaDialect.Lua50, targetSizeT: 8);
            var binder = new BND4 { Compression = new DCX.DcxDfltCompressionInfo(DCX.DfltCompressionPreset.DCX_DFLT_10000_44_9) };
            binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, @"N:\script\area.luainfo", info.ToBytes()));
            binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 1, @"N:\script\area.luagnl", gnl.ToBytes()));
            binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 2, @"N:\script\100000_battle.lua", script));
            binder.Write(path);
            var archive = LuabndArchive.TryOpen(path)!;
            var table = LuaInfoDocument.FromArchive(archive, archive.Entries[0].Name);
            table.Goals[0].Name = "EditedBattle";
            table.SaveToArchive();
            var globals = LuaGnlDocument.FromArchive(archive, archive.Entries[1].Name);
            globals.Globals.Add("EditedBattle_Activate");
            globals.SaveToArchive();
            var reopened = LuabndArchive.TryOpen(path)!;
            Assert.True(DCX.Is(File.ReadAllBytes(path)));
            Assert.Equal(binder.Compression, BND4.Read(path).Compression);
            Assert.Equal(binder.Files.Select(f => f.ID), BND4.Read(path).Files.Select(f => f.ID));
            Assert.False(reopened.IsBnd3);
            Assert.Equal(script, reopened.Entries[2].Bytes);
            Assert.Equal("EditedBattle", LuaInfoDocument.FromArchive(reopened, reopened.Entries[0].Name).Goals[0].Name);
            Assert.True(LuaGnlDocument.FromArchive(reopened, reopened.Entries[1].Name).LongFormat);
            var goal = Ds1AiCatalog.FromArchive(reopened).FindPair(100000).Battle!;
            Assert.Equal(reopened.Entries[0].Name, goal.EntryName);
            Assert.Same(reopened, goal.Archive);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Ds3SymbolsComeFromItsOwnDatabase()
    {
        var ds3 = AiNameDatabase.LoadEmbedded(GameMode.DS3);
        Assert.True(ds3.KnownSymbols.Count > 10000);
        Assert.Contains("GOAL_COMMON_TopGoal", ds3.KnownSymbols);
        Assert.Contains("GetDist", ds3.KnownSymbols);
        Assert.Empty(ds3.Calls); // No invented signatures for harvested names.
    }

    [Fact]
    public void GoalDiscoveryFiltersOtherGames()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        try
        {
            var ds1 = LuaInfoDocument.CreateNew();
            ds1.Goals[0].Name = "OnlyDS1";
            ds1.Save(Path.Combine(folder, "ds1.luainfo"));
            var ds3 = LuaInfoDocument.CreateNew(GameMode.DS3);
            ds3.Goals[0].Name = "OnlyDS3";
            ds3.Save(Path.Combine(folder, "ds3.luainfo"));
            var catalog = Ds1AiCatalog.DiscoverNearDirectory(folder, levelsUp: 0, mode: GameMode.DS3);
            Assert.NotNull(catalog.FindGoal("OnlyDS3"));
            Assert.Null(catalog.FindGoal("OnlyDS1"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
