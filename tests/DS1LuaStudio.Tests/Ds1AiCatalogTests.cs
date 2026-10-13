using DS1LuaStudio.Core;
using SoulsFormats;

namespace DS1LuaStudio.Tests;

public class Ds1AiCatalogTests
{
    /// <summary>Mirrors the shape of a stock PTDE goal table.</summary>
    private static Ds1AiCatalog Catalog(params (int Id, string Name, bool Battle, bool Logic)[] goals)
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.Goals.Clear();
        foreach (var (id, name, battle, logic) in goals)
            doc.AddGoal(new GoalRow(new LUAINFO.Goal(id, name, battle, logic), 0));

        return Ds1AiCatalog.FromDocuments(new[] { doc });
    }

    private static Ds1AiCatalog Stock() => Catalog(
        (251000, "HusibitoKojiki251000Battle", true, false),
        (251000, "HusibitoKojiki251000_Logic", false, true),
        (6030, "BigHat_Logarn6030Battle", true, false),
        (351000, "BigCrow351000Battle", true, false));

    [Fact]
    public void GoalCallbackSuffixesResolveToAiAndGoal()
    {
        var catalog = Stock();

        foreach (string suffix in new[] { "Activate", "Update", "Terminate", "Interupt" })
        {
            var info = catalog.Resolve($"HusibitoKojiki251000Battle_{suffix}", 2);
            Assert.NotNull(info);
            Assert.Equal(new[] { "ai", "goal" }, info!.ParameterNames);
            Assert.Equal(251000, info.Goal?.Id);
        }
    }

    [Fact]
    public void ActCallbacksResolveToTheThreeArgumentSignature()
    {
        var catalog = Stock();
        var info = catalog.Resolve("BigHat_Logarn6030_Act01", 3);

        Assert.NotNull(info);
        Assert.Equal(new[] { "ai", "goal", "paramTbl" }, info!.ParameterNames);
        Assert.Equal("BigHat_Logarn6030Battle", info.GoalName);
        Assert.Equal(6030, info.Goal?.Id);
    }

    [Fact]
    public void LogicCallbackTakesOnlyTheAiHandle()
    {
        var catalog = Stock();
        var info = catalog.Resolve("HusibitoKojiki251000_Logic", 1);

        Assert.NotNull(info);
        Assert.Equal(new[] { "ai" }, info!.ParameterNames);
    }

    [Fact]
    public void BattleSuffixIsStrippedForActCallbacks()
    {
        // The goal is "BigHat_Logarn6030Battle" but the act callbacks use the bare group name.
        var catalog = Stock();
        var info = catalog.Resolve("BigHat_Logarn6030_ActAfter_AdjustSpace", 3);

        Assert.NotNull(info);
        Assert.Equal(6030, info.Goal?.Id);
        Assert.Equal("ActAfter_AdjustSpace", info!.Suffix);
    }

    [Fact]
    public void LogicSuffixIsStrippedForLogicCallbacks()
    {
        var catalog = Stock();
        Assert.NotNull(catalog.Resolve("HusibitoKojiki251000_Interupt", 2));
    }

    [Fact]
    public void UnknownNamesDoNotResolve()
    {
        var catalog = Stock();
        Assert.Null(catalog.Resolve("SomeUnrelatedFunction", 2));
        Assert.Null(catalog.Resolve(null, 2));
        Assert.Null(catalog.Resolve("NoUnderscoreHere", 0));
    }

    [Fact]
    public void ArityIsCheckedBeforeNaming()
    {
        var catalog = Stock();

        // An _Activate taking three arguments is a signature we have not identified. The goal is
        // still linked, but no parameter names are offered so nothing gets renamed.
        var info = catalog.Resolve("BigCrow351000Battle_Activate", 3);

        Assert.NotNull(info);
        Assert.Equal(351000, info!.Goal?.Id);
        Assert.False(info.HasRealParameterNames);
        Assert.Empty(info.ParameterNames);
    }

    [Fact]
    public void LongestGoalNameWinsWhenPrefixesOverlap()
    {
        var catalog = Catalog(
            (1, "Knight", true, false),
            (2, "Knight_of_HolyWomanA6080Battle", true, false));

        var info = catalog.Resolve("Knight_of_HolyWomanA6080Battle_Activate", 2);

        Assert.NotNull(info);
        Assert.Equal("Knight_of_HolyWomanA6080Battle", info!.GoalName);
        Assert.Equal(2, info.Goal?.Id);
    }

    [Fact]
    public void EmptyCatalogResolvesNothing()
    {
        Assert.Null(Ds1AiCatalog.Empty.Resolve("Anything_Activate", 2));
        Assert.Equal(0, Ds1AiCatalog.Empty.GoalCount);
    }

    [Fact]
    public void DiscoverNear_ReadsGoalTablesInParentFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        string deep = Path.Combine(root, "area-luabnd", "ai", "out", "bin");
        Directory.CreateDirectory(deep);

        try
        {
            var info = new LUAINFO(false, false);
            info.Goals.Add(new LUAINFO.Goal(4242, "TestEnemy4242Battle", true, false));
            info.Write(Path.Combine(root, "area-luabnd", "area.luainfo"));

            string script = Path.Combine(deep, "004242_battle.lua");
            File.WriteAllBytes(script, LuaBytecodeService.Compile("return 1\n", LuaDialect.Lua50, System.Text.Encoding.UTF8));

            var catalog = Ds1AiCatalog.DiscoverNear(script);
            Assert.Equal(1, catalog.GoalCount);
            Assert.NotNull(catalog.Resolve("TestEnemy4242Battle_Activate", 2));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A script extracted from a .luabnd has no path of its own, so the search has to be able to start
    /// from the folder holding the archive. This is the path the editor takes for archive entries.
    /// </summary>
    [Fact]
    public void DiscoverNearDirectory_FindsTheSameTablesAsDiscoverNear()
    {
        string root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        string deep = Path.Combine(root, "area-luabnd", "ai", "out", "bin");
        Directory.CreateDirectory(deep);

        try
        {
            var info = new LUAINFO(false, false);
            info.Goals.Add(new LUAINFO.Goal(4242, "TestEnemy4242Battle", true, false));
            info.Write(Path.Combine(root, "area-luabnd", "area.luainfo"));

            var catalog = Ds1AiCatalog.DiscoverNearDirectory(deep);

            Assert.Equal(1, catalog.GoalCount);
            Assert.NotNull(catalog.Resolve("TestEnemy4242Battle_Activate", 2));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DiscoverNear_ReturnsEmptyWhenThereAreNoGoalTables()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string script = Path.Combine(dir, "x.lua");
            File.WriteAllText(script, "return 1");

            // Only this folder is inspected, so unrelated goal tables elsewhere cannot interfere.
            Assert.Equal(0, Ds1AiCatalog.DiscoverNear(script, levelsUp: 0).GoalCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class Ds1ArgumentNamerTests
{
    private static Ds1AiCatalog Catalog()
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.Goals.Clear();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000Battle", true, false), 0));
        return Ds1AiCatalog.FromDocuments(new[] { doc });
    }

    private const string Source = """
        local f0_local0 = 0

        function HusibitoKojiki251000Battle_Activate(f1_arg0, f1_arg1)
            local f1_local0 = f1_arg0:GetDist(f1_arg1)
            return f1_local0
        end

        function Unrelated_Helper(f2_arg0)
            return f2_arg0
        end
        """;

    [Fact]
    public void ParseDeclarations_ReadsNamesIdsAndArity()
    {
        var parsed = Ds1ArgumentNamer.ParseDeclarations(Source);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("HusibitoKojiki251000Battle_Activate", parsed[0].Name);
        Assert.Equal(1, parsed[0].FunctionId);
        Assert.Equal(2, parsed[0].Arity);
        Assert.Equal("Unrelated_Helper", parsed[1].Name);
        Assert.Equal(2, parsed[1].FunctionId);
    }

    [Fact]
    public void Apply_RenamesOnlyResolvedCallbacks()
    {
        var result = Ds1ArgumentNamer.Apply(Source, Catalog());

        Assert.Contains("function HusibitoKojiki251000Battle_Activate(ai, goal)", result.Source);
        Assert.Contains("local f1_local0 = ai:GetDist(goal)", result.Source);

        // The unrelated helper keeps its generated names.
        Assert.Contains("function Unrelated_Helper(f2_arg0)", result.Source);
    }

    [Fact]
    public void Apply_LeavesGeneratedNamesWhenNoGoalsAreLinked()
    {
        var result = Ds1ArgumentNamer.Apply(Source, Ds1AiCatalog.Empty);

        Assert.Equal(Source, result.Source);
        Assert.Equal(0, result.RenamedIdentifiers);
    }

    [Fact]
    public void Apply_ReportsWhatItResolved()
    {
        var result = Ds1ArgumentNamer.Apply(Source, Catalog());

        Assert.Single(result.Resolved);
        Assert.Equal(251000, result.Resolved[0].Goal?.Id);
        Assert.True(result.RenamedIdentifiers > 0);
    }

    [Fact]
    public void Apply_RefusesWhenANewNameWouldShadowAnExistingIdentifier()
    {
        // "goal" is already used as an identifier, so renaming f1_arg1 would shadow it.
        const string conflicting = """
            local goal = nil
            function HusibitoKojiki251000Battle_Activate(f1_arg0, f1_arg1)
                return f1_arg0, f1_arg1, goal
            end
            """;

        var result = Ds1ArgumentNamer.Apply(conflicting, Catalog());

        Assert.Equal(conflicting, result.Source);
        Assert.Equal(0, result.RenamedIdentifiers);
    }

    [Fact]
    public void Apply_HandlesUpvalueStyleReferencesFromOuterFunctions()
    {
        // A nested function refers to the outer callback's parameters by their generated names,
        // so a single whole-file rename must keep them consistent.
        const string nested = """
            function HusibitoKojiki251000Battle_Activate(f1_arg0, f1_arg1)
                local function helper()
                    return f1_arg0
                end
                return helper(), f1_arg1
            end
            """;

        var result = Ds1ArgumentNamer.Apply(nested, Catalog());

        Assert.Contains("return ai", result.Source);
        Assert.Contains("return helper(), goal", result.Source);
        Assert.DoesNotContain("f1_arg", result.Source);
    }

    [Fact]
    public void Apply_HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, Ds1ArgumentNamer.Apply(string.Empty, Catalog()).Source);
        Assert.Equal(0, Ds1ArgumentNamer.Apply("return 1\n", Catalog()).RenamedIdentifiers);
    }
}
