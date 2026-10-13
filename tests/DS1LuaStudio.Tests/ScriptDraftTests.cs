using DS1LuaStudio.Core;

namespace DS1LuaStudio.Tests;

public class ScriptDraftTests
{
    [Fact]
    public void CapturesEntireActIncludingNestedBlocksAndAnonymousHelpers()
    {
        const string function = """
function Enemy_Act04(ai, goal, paramTbl)
    -- end function if repeat
    local message = "function fake() end"
    local description = [==[ end if function ]==]
    local helper = function()
        for i = 1, 3 do
            while i > 0 do
                do local unused = i end
                break
            end
        end
    end
    if ai then
        repeat
            helper()
        until true
    elseif goal then
        return 1
    else
        return 0
    end
end
""";
        string source = "-- before\n" + function + "\nfunction Next() return 1 end";
        var captured = LuaFunctionDrafts.AtCaret(source, source.IndexOf("helper()", StringComparison.Ordinal))!;
        Assert.Equal("Enemy_Act04", captured.Name);
        Assert.Equal(function, captured.Extract(source));
        Assert.Equal("Next", LuaFunctionDrafts.FindFunctions(source).Last().Name);
    }

    [Fact]
    public void SupportsLocalAssignedFunctionsAndIgnoresLongComments()
    {
        const string source = "--[=[ function Fake() end ]=]\nlocal Act04 = function(ai)\n return ai\nend\n";
        var function = Assert.Single(LuaFunctionDrafts.FindFunctions(source));
        Assert.Equal("Act04", function.Name);
        Assert.StartsWith("local Act04 = function", function.Extract(source));
    }

    [Fact]
    public void IncompleteFunctionsAreNotCopied() => Assert.Null(LuaFunctionDrafts.AtCaret("function Act04(ai)\n if ai then return ai end", 20));

    [Fact]
    public void CapturedActionsListReferencedOuterConstants()
    {
        const string source = "local distance, unused = 3, 4\nfunction Act04(ai, goal)\n return distance + ai:GetDist(0)\nend";
        var function = Assert.Single(LuaFunctionDrafts.FindFunctions(source));
        Assert.Equal(new[] { "distance" }, LuaFunctionDrafts.ReferencedOuterLocals(source, function));
    }

    [Fact]
    public void RenamePreservesStringsCommentsAndMemberCalls()
    {
        const string source = "function Old(ai) -- Old\n local s = 'Old'\n ai:Old()\n return Old(ai)\nend";
        var result = LuaFunctionDrafts.Rename(source, "Old", "NewAct04");
        Assert.Contains("function NewAct04", result);
        Assert.Contains("return NewAct04(ai)", result);
        Assert.Contains("ai:Old()", result);
        Assert.Contains("-- Old", result);
        Assert.Contains("'Old'", result);
        Assert.Throws<ArgumentException>(() => LuaFunctionDrafts.Rename(source, "Old", "end"));
        Assert.Throws<ArgumentException>(() => LuaFunctionDrafts.Rename(source, "Old", "ai"));
    }

    [Fact]
    public void DraftsPersistAcrossNewStoreInstancesAndDamagedFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new ScriptDraftStore(folder);
            var draft = new ScriptDraft { Name = "Act04 heavy swing", GameMode = GameMode.DS3, Source = "function Enemy_Act04(ai) return ai end", Origin = "area.luabnd :: 100000_battle.lua", Notes = "Requires matching animation IDs" };
            store.Save(draft);
            File.WriteAllText(Path.Combine(folder, "damaged.json"), "broken");
            var loaded = Assert.Single(new ScriptDraftStore(folder).Load());
            Assert.Equal(draft, loaded);
            store.Save(draft with { Notes = "Updated notes" });
            Assert.Equal("Updated notes", Assert.Single(store.Load()).Notes);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void EveryTemplateCompilesAndDecompilesAtTheGameWidth(int width)
    {
        foreach (var template in EnemyScriptTemplates.All)
        {
            string source = template.Render("MyEnemy100000", 4, 3000, 3001, 3.5, 0.5);
            Assert.DoesNotContain("{{", source);
            var bytes = LuaBytecodeService.Compile(source, LuaDialect.Lua50, targetSizeT: width);
            Assert.Equal(width, LuaBytecodeService.ReadSizeTSize(bytes));
            Assert.True(LuaBytecodeService.Decompile(bytes).Success, template.Name);
            Assert.NotEmpty(LuaFunctionDrafts.FindFunctions(source));
        }
    }
}
