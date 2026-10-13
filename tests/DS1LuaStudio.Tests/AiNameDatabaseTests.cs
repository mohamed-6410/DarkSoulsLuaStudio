using DS1LuaStudio.Core;

namespace DS1LuaStudio.Tests;

public class AiNameDatabaseTests
{
    private static AiNameDatabase Db() => AiNameDatabase.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_PopulatesAllSections()
    {
        var db = Db();
        Assert.NotEmpty(db.GlobalTranslations);
        Assert.NotEmpty(db.FunctionParameters);
        Assert.NotEmpty(db.Calls);
        Assert.NotEmpty(db.Goals);
    }

    [Fact]
    public void KnownSymbols_IncludesGoalsAndFunctions()
    {
        var db = Db();
        Assert.Contains("GlobalActivate", db.KnownSymbols);
        Assert.Contains("GOAL_COMMON_Stay", db.KnownSymbols);
        Assert.Contains("GetDist", db.KnownSymbols);
    }

    [Fact]
    public void DescribeFunction_ListsParameters()
    {
        var db = Db();
        Assert.Equal("function GlobalUpdate(ai, goal, dT)", db.DescribeFunction("GlobalUpdate"));
    }

    [Fact]
    public void DescribeGoal_ListsFields()
    {
        var db = Db();
        var text = db.DescribeGoal("GOAL_COMMON_Stay");
        Assert.NotNull(text);
        Assert.Contains("enemyReactDist", text);
    }

    [Fact]
    public void DescribeCall_ShowsArgsAndReturns()
    {
        var db = Db();
        var text = db.DescribeCall("GetDist");
        Assert.NotNull(text);
        Assert.Contains("distTarget", text);
        Assert.Contains("distance", text);
    }

    [Fact]
    public void Describe_FallsBackToGlobalTranslation()
    {
        var db = Db();
        var text = db.Describe("AI_DIR_TYPE_FR");
        Assert.NotNull(text);
        Assert.Contains("FrontRight", text);
    }

    [Fact]
    public void Describe_ReturnsNullForUnknownSymbol()
    {
        Assert.Null(Db().Describe("definitely_not_a_ds1_symbol"));
    }

    [Fact]
    public void MalformedJson_FallsBackToEmpty()
    {
        var db = AiNameDatabase.Load(new MemoryStream("{ not json"u8.ToArray()));
        Assert.Empty(db.KnownSymbols);
    }
}
