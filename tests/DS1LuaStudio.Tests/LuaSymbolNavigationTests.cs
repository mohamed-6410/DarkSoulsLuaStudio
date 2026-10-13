using DS1LuaStudio.Core;
namespace DS1LuaStudio.Tests;

public class LuaSymbolNavigationTests
{
    [Theory]
    [InlineData("-- function Target()\nfunction Target() end", 30)]
    [InlineData("local s = 'function Target()'\nTarget = function() end", 30)]
    [InlineData("--[=[ function Target() ]=]\nfunction Target() end", 37)]
    public void SkipsCommentsAndStrings(string source, int offset) =>
        Assert.Equal(offset, LuaSymbolNavigation.FindDefinition(source, "Target"));

    [Fact]
    public void CallsAreNotDefinitions() => Assert.Equal(-1, LuaSymbolNavigation.FindDefinition("Target(ai)", "Target"));

    [Fact]
    public void BytecodeIndexDistinguishesDefinitionsFromCalls()
    {
        var definition = LuaBytecodeService.Compile("function Target() return 1 end", LuaDialect.Lua50, targetSizeT: 8);
        var call = LuaBytecodeService.Compile("Target()", LuaDialect.Lua50, targetSizeT: 8);
        Assert.True(LuaSymbolNavigation.HasGlobalDefinition(definition, "Target"));
        Assert.False(LuaSymbolNavigation.HasGlobalDefinition(call, "Target"));
    }
}
