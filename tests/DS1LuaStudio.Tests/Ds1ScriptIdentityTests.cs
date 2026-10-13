using DS1LuaStudio.Core;
using SoulsFormats;

namespace DS1LuaStudio.Tests;

public class Ds1ScriptIdentityTests
{
    [Theory]
    [InlineData(@"N:\FRPG\script\ai\out\bin\251000_battle.lua", 251000, AiScriptKind.Battle)]
    [InlineData("251000_logic.lua", 251000, AiScriptKind.Logic)]
    [InlineData("6030_logic.LUA", 6030, AiScriptKind.Logic)]
    public void RecognisesAiScriptFileNames(string path, int id, AiScriptKind kind)
    {
        var identity = Ds1ScriptIdentity.TryParse(path);

        Assert.NotNull(identity);
        Assert.Equal(id, identity!.GoalId);
        Assert.Equal(kind, identity.Kind);
    }

    /// <summary>A script taken out of a .luabnd has no path, only an entry name.</summary>
    [Fact]
    public void WorksForAnArchiveEntryName() =>
        Assert.Equal(279000, Ds1ScriptIdentity.TryParse("279000_battle.lua")!.GoalId);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("m10_02_00_00.luabnd")]       // an archive, not a script
    [InlineData("battle.lua")]                // no ID prefix
    [InlineData("251000_battle_extra.lua")]   // too many segments
    [InlineData("abc_battle.lua")]            // ID is not a number
    [InlineData("251000_common.lua")]         // neither half of the group
    [InlineData("_battle.lua")]               // no ID
    public void RejectsEverythingElse(string? path) => Assert.Null(Ds1ScriptIdentity.TryParse(path));

    [Fact]
    public void LabelsReadAsBattleOrLogic()
    {
        Assert.Equal("Battle", Ds1ScriptIdentity.TryParse("251000_battle.lua")!.Label);
        Assert.Equal("Logic", Ds1ScriptIdentity.TryParse("251000_logic.lua")!.Label);
    }
}

public class ScriptGoalLinkTests
{
    private static Ds1ScriptIdentity Battle(int id) => new(id, AiScriptKind.Battle);
    private static Ds1ScriptIdentity Logic(int id) => new(id, AiScriptKind.Logic);

    [Fact]
    public void LinksToTheHalfTheScriptImplements()
    {
        var link = ScriptGoalLink.From(Battle(251000), Catalog().FindPair(251000));

        Assert.Equal("HusibitoKojiki251000Battle", link.Name);
        Assert.Equal("Battle 251000", link.Caption);
        Assert.True(link.Exists);
    }

    /// <summary>A _battle script must never resolve to the logic row, and vice versa.</summary>
    [Fact]
    public void NeverLinksAcrossHalves()
    {
        var catalog = Catalog();

        Assert.Equal("HusibitoKojiki251000_Logic", ScriptGoalLink.From(Logic(251000), catalog.FindPair(251000)).Name);
        Assert.Equal("BigHat_Logarn6030Battle", ScriptGoalLink.From(Battle(6030), catalog.FindPair(6030)).Name);
    }

    /// <summary>Plenty of enemies ship only a battle script, so the logic half is often absent.</summary>
    [Fact]
    public void ReportsAMissingRowWithoutFailing()
    {
        var link = ScriptGoalLink.From(Logic(6030), Catalog().FindPair(6030));

        Assert.False(link.Exists);
        Assert.Equal("Logic 6030", link.Display);
        Assert.Contains("no row", link.ToolTip);
    }

    [Fact]
    public void FallsBackToTheCaptionForAnUnknownGroup()
    {
        var link = ScriptGoalLink.From(Battle(279000), Catalog().FindPair(279000));

        Assert.False(link.Exists);
        Assert.Equal("Battle 279000", link.Caption);
    }

    private static Ds1AiCatalog Catalog()
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000Battle", true, false), 0));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000_Logic", false, true), 0));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(6030, "BigHat_Logarn6030Battle", true, false), 0));
        return Ds1AiCatalog.FromDocuments(new[] { doc });
    }
}

public class LuaInfoPairLookupTests
{
    private static LuaInfoDocument Stock()
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000Battle", true, false), 0));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000_Logic", false, true), 0));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(6030, "BigHat_Logarn6030Battle", true, false), 0));
        return doc;
    }

    [Fact]
    public void FindsTheBattleAndLogicRowsOfOneGroup()
    {
        var (battle, logic) = Stock().FindPair(251000);

        Assert.Equal("HusibitoKojiki251000Battle", battle!.Name);
        Assert.Equal("HusibitoKojiki251000_Logic", logic!.Name);
    }

    [Fact]
    public void ReportsNullForTheHalfThatIsMissing()
    {
        var (battle, logic) = Stock().FindPair(6030);

        Assert.Equal("BigHat_Logarn6030Battle", battle!.Name);
        Assert.Null(logic);
    }

    [Fact]
    public void ReportsBothNullForAnUnknownGroup()
    {
        var (battle, logic) = Stock().FindPair(279000);

        Assert.Null(battle);
        Assert.Null(logic);
    }

    /// <summary>The link needs the row to be selectable, so a new pair has to be findable at once.</summary>
    [Fact]
    public void AFreshlyAddedPairIsImmediatelyFindable()
    {
        var doc = Stock();
        var (battle, logic) = doc.AddGoalPair(279000);

        var (foundBattle, foundLogic) = doc.FindPair(279000);
        Assert.Same(battle, foundBattle);
        Assert.Same(logic, foundLogic);
    }
}