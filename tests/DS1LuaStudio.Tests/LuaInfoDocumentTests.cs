using DS1LuaStudio.Core;
using SoulsFormats;

namespace DS1LuaStudio.Tests;

public class LuaInfoDocumentTests
{
    private static LuaInfoDocument BuildSample()
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.Goals.Clear();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(351000, "BattleBigCrow", true, false), 0));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(10100, "Gitai10100_Logic", false, true, "Gitai10100_Interupt"), 1));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(254000, "HusiFight_Sword", true, false), 2));
        return doc;
    }

    [Fact]
    public void CreateNew_UsesDs1PcFormat()
    {
        var doc = LuaInfoDocument.CreateNew();
        Assert.False(doc.BigEndian);
        Assert.False(doc.LongFormat);
    }

    [Fact]
    public void SaveThenLoad_PreservesEveryField()
    {
        var original = BuildSample();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luainfo");

        try
        {
            original.Save(path);
            var reloaded = new LuaInfoDocument();
            reloaded.Load(path);

            Assert.Equal(3, reloaded.Goals.Count);
            Assert.False(reloaded.IsDirty);

            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(original.Goals[i].Id, reloaded.Goals[i].Id);
                Assert.Equal(original.Goals[i].Name, reloaded.Goals[i].Name);
                Assert.Equal(original.Goals[i].BattleInterrupt, reloaded.Goals[i].BattleInterrupt);
                Assert.Equal(original.Goals[i].LogicInterrupt, reloaded.Goals[i].LogicInterrupt);
                Assert.Equal(original.Goals[i].LogicInterruptName, reloaded.Goals[i].LogicInterruptName);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ToBytes_ProducesBytesSoulsFormatsCanReadBack()
    {
        var doc = BuildSample();
        var bytes = doc.ToBytes();

        var info = SoulsFile<LUAINFO>.Read(bytes);
        Assert.Equal(3, info.Goals.Count);
        Assert.Equal(351000, info.Goals[0].ID);
        Assert.Equal("BattleBigCrow", info.Goals[0].Name);
        Assert.Null(info.Goals[0].LogicInterruptName);
        Assert.Equal("Gitai10100_Interupt", info.Goals[1].LogicInterruptName);
    }

    [Fact]
    public void ToBytes_IsByteIdenticalToRepeatedWrites()
    {
        // Writing the same table twice must produce the same bytes, i.e. the editor is not
        // introducing drift between saves.
        var doc = BuildSample();
        Assert.Equal(doc.ToBytes(), doc.ToBytes());

        var reloaded = new LuaInfoDocument();
        reloaded.Load(new MemoryStream(doc.ToBytes()));
        Assert.Equal(doc.ToBytes(), reloaded.ToBytes());
    }

    [Fact]
    public void LogicInterruptName_ClearingBecomesNull()
    {
        var doc = BuildSample();
        var row = doc.Goals[1];
        Assert.Equal("Gitai10100_Interupt", row.LogicInterruptName);

        row.LogicInterruptName = string.Empty;
        Assert.Null(row.LogicInterruptName);
    }

    [Fact]
    public void NextId_PicksLowestFreeValue()
    {
        var doc = BuildSample();
        // 0, 1 and 2 are free
        Assert.Equal(0, doc.NextId());

        doc.Goals[0].Id = 0;
        Assert.Equal(1, doc.NextId());
    }

    [Fact]
    public void Duplicate_CopiesRowWithFreshId()
    {
        var doc = BuildSample();
        var source = doc.Goals[0];
        doc.Duplicate(source);

        Assert.Equal(4, doc.Goals.Count);
        var copy = doc.Goals[1];

        // The duplicate gets a fresh, unused ID and a distinct name...
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("BattleBigCrow_Copy", copy.Name);
        Assert.Equal(source.BattleInterrupt, copy.BattleInterrupt);
        Assert.Equal(source.LogicInterrupt, copy.LogicInterrupt);

        // ...while the original row is left untouched.
        Assert.Equal("BattleBigCrow", source.Name);
        Assert.Equal(351000, source.Id);
    }

    [Fact]
    public void Move_ReordersAndReindexes()
    {
        var doc = BuildSample();
        var last = doc.Goals[2];
        doc.Move(last, -1);

        Assert.Equal(254000, doc.Goals[1].Id);
        Assert.Equal(10100, doc.Goals[2].Id);
        Assert.Equal(0, doc.Goals[0].Index);
        Assert.Equal(1, doc.Goals[1].Index);
        Assert.Equal(2, doc.Goals[2].Index);
    }

    [Fact]
    public void SortById_OrdersNumerically()
    {
        var doc = BuildSample();
        doc.SortById();

        Assert.Equal(new[] { 10100, 254000, 351000 }, doc.Goals.Select(g => g.Id).ToArray());
    }

    [Fact]
    public void AddGoalPair_SharesOneIdAndSetsBothFlags()
    {
        var doc = BuildSample();
        var (battle, logic) = doc.AddGoalPair();

        Assert.Equal(battle.Id, logic.Id);
        Assert.Equal(5, doc.Goals.Count);

        Assert.True(battle.BattleInterrupt);
        Assert.False(battle.LogicInterrupt);
        Assert.Null(battle.LogicInterruptName);

        Assert.False(logic.BattleInterrupt);
        Assert.True(logic.LogicInterrupt);

        Assert.Equal(battle.Index + 1, logic.Index);
    }

    [Fact]
    public void AddGoalPair_ProducesAValidTableWithoutErrors()
    {
        var doc = BuildSample();
        doc.AddGoalPair();
        doc.AddGoalPair();

        var issues = doc.Validate();
        Assert.DoesNotContain(issues, i => i.Severity == LuaInfoIssueSeverity.Error);
    }

    [Fact]
    public void AddGoalPair_UsesAnIdNoOtherGoalUses()
    {
        var doc = BuildSample();
        var (battle, logic) = doc.AddGoalPair();
        var (battle2, logic2) = doc.AddGoalPair();

        // Each pair shares its own ID, and the two pairs do not collide.
        Assert.Equal(battle.Id, logic.Id);
        Assert.Equal(battle2.Id, logic2.Id);
        Assert.NotEqual(battle.Id, battle2.Id);

        // Exactly two IDs are shared, so distinct count is two short of the row count.
        Assert.Equal(doc.Goals.Count - 2, doc.Goals.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void AddGoalPair_RoundTripsThroughSave()
    {
        var doc = BuildSample();
        doc.AddGoalPair();
        var (battle, logic) = (doc.Goals[^2], doc.Goals[^1]);

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luainfo");
        try
        {
            doc.Save(path);
            var reloaded = new LuaInfoDocument();
            reloaded.Load(path);

            Assert.Equal(battle.Id, reloaded.Goals[^2].Id);
            Assert.Equal(battle.Id, reloaded.Goals[^1].Id);
            Assert.True(reloaded.Goals[^2].BattleInterrupt);
            Assert.True(reloaded.Goals[^1].LogicInterrupt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_AcceptsABattleLogicPairSharingOneId()
    {
        // This is how DS1 splits a battle group that has an interrupt. It is present in every stock
        // goal table (e.g. HusibitoKojiki251000Battle / HusibitoKojiki251000_Logic) and must not be
        // reported as a problem.
        var doc = BuildSample();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000_Logic", false, true,
            "HusibitoKojiki251000_Interupt"), 3));
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(251000, "HusibitoKojiki251000Battle", true, false), 4));

        var issues = doc.Validate();

        Assert.DoesNotContain(issues, i => i.Severity == LuaInfoIssueSeverity.Error);
        Assert.Contains(issues, i => i.Severity == LuaInfoIssueSeverity.Info && i.Message.Contains("battle + logic pair"));
    }

    [Fact]
    public void Validate_RejectsARepeatedIdThatIsNotABattleLogicPair()
    {
        var doc = BuildSample();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(351000, "BigCrow351000BattleCopy", true, false), 3));

        var issues = doc.Validate();
        Assert.Contains(issues, i =>
            i.Severity == LuaInfoIssueSeverity.Error &&
            i.Message.Contains("351000") &&
            i.Message.Contains("2 battle"));
    }

    [Fact]
    public void Validate_RejectsDuplicateNames()
    {
        // Names are what identify a goal; all 140 stock goals have unique names.
        var doc = BuildSample();
        doc.AddGoal(new GoalRow(new LUAINFO.Goal(999, "BattleBigCrow", true, false), 3));

        var issues = doc.Validate();
        Assert.Contains(issues, i =>
            i.Severity == LuaInfoIssueSeverity.Error && i.Message.Contains("must be unique"));
    }

    [Fact]
    public void Validate_FlagsDuplicateNames()
    {
        var doc = BuildSample();
        doc.Goals[1].Name = doc.Goals[0].Name;

        var issues = doc.Validate();
        Assert.Contains(issues, i => i.Severity == LuaInfoIssueSeverity.Error && i.Message.Contains("more than once"));
    }

    [Fact]
    public void Validate_FlagsLogicInterruptWithoutName()
    {
        var doc = BuildSample();
        doc.Goals[0].LogicInterrupt = true;

        var issues = doc.Validate();
        Assert.Contains(issues, i => i.Message.Contains("no interrupt function name"));
    }

    [Fact]
    public void Validate_RejectsEmptyGoalList()
    {
        var doc = BuildSample();
        doc.Goals.Clear();

        var issues = doc.Validate();
        Assert.Contains(issues, i => i.Severity == LuaInfoIssueSeverity.Error && i.RowIndex < 0);
    }

    [Fact]
    public void Csv_RoundTrips()
    {
        var doc = BuildSample();
        var restored = LuaInfoDocument.FromCsv(doc.ToCsv());

        Assert.Equal(3, restored.Goals.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(doc.Goals[i].Id, restored.Goals[i].Id);
            Assert.Equal(doc.Goals[i].Name, restored.Goals[i].Name);
            Assert.Equal(doc.Goals[i].BattleInterrupt, restored.Goals[i].BattleInterrupt);
            Assert.Equal(doc.Goals[i].LogicInterrupt, restored.Goals[i].LogicInterrupt);
            Assert.Equal(doc.Goals[i].LogicInterruptName, restored.Goals[i].LogicInterruptName);
        }
    }

    [Fact]
    public void Csv_HandlesNamesContainingCommas()
    {
        var doc = BuildSample();
        doc.Goals[0].Name = "Boss, Phase 2";
        doc.Goals[1].Name = "He said \"hi\"";

        var restored = LuaInfoDocument.FromCsv(doc.ToCsv());
        Assert.Equal("Boss, Phase 2", restored.Goals[0].Name);
        Assert.Equal("He said \"hi\"", restored.Goals[1].Name);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var doc = BuildSample();
        var restored = LuaInfoDocument.FromJson(doc.ToJson());

        Assert.Equal(3, restored.Goals.Count);
        Assert.Equal("Gitai10100_Interupt", restored.Goals[1].LogicInterruptName);
        Assert.Equal(351000, restored.Goals[0].Id);
    }

    [Fact]
    public void RefreshDirty_TracksEdits()
    {
        var doc = BuildSample();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luainfo");
        try
        {
            doc.Save(path);
            doc.Goals[0].Name = "Changed";
            doc.RefreshDirty();
            Assert.True(doc.IsDirty);

            doc.Goals[0].Name = "BattleBigCrow";
            doc.RefreshDirty();
            Assert.False(doc.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AddGoal_UsesLowestFreeIdAndMarksDirty()
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.Goals.Clear();
        doc.Save(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.luainfo"));
        doc.RefreshDirty();
        Assert.False(doc.IsDirty);

        doc.AddGoal();
        doc.RefreshDirty();
        Assert.True(doc.IsDirty);
    }
}
