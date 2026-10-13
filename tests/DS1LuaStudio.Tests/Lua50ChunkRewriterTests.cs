using System.Text;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.Tests;

/// <summary>
/// The bundled Lua 5.0 compiler emits 64-bit size_t string lengths, while Dark Souls PTDE's Lua
/// uses 32-bit. The rewriter bridges that gap so recompiled scripts can be shipped.
/// </summary>
public class Lua50ChunkRewriterTests
{
    private const string Source = """
        local function fight(ai, goal)
            local t = { 1, 2, 3 }
            for i = 1, 3 do
                goal:AddGoal(t[i])
            end
            return t
        end

        function GlobalActivate(ai, goal)
            fight(ai, goal)
        end
        """;

    private static byte[] Compile(int? sizeT = null) =>
        LuaBytecodeService.Compile(Source, LuaDialect.Lua50, Encoding.UTF8, sizeT);

    [Fact]
    public void BundledCompiler_Emits64BitSizeT()
    {
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(Compile()));
        Assert.Equal(8, LuaBytecodeService.CompilerSizeTSize(LuaDialect.Lua50));
    }

    [Fact]
    public void ConvertSizeT_ProducesA32BitHeader()
    {
        var converted = Lua50ChunkRewriter.ConvertSizeT(Compile(), 4);
        Assert.Equal(4, LuaBytecodeService.ReadSizeTSize(converted));
        Assert.Equal(LuaDialect.Lua50, LuaBytecodeService.Detect(converted));
    }

    [Fact]
    public void ConvertSizeT_IsReversible()
    {
        var original = Compile();
        var to32 = Lua50ChunkRewriter.ConvertSizeT(original, 4);
        var back = Lua50ChunkRewriter.ConvertSizeT(to32, 8);

        Assert.Equal(original, back);
    }

    [Fact]
    public void ConvertSizeT_ShrinksTheChunk()
    {
        var original = Compile();
        var converted = Lua50ChunkRewriter.ConvertSizeT(original, 4);

        // Every length prefix shrinks by 4 bytes, so the chunk must get smaller.
        Assert.True(converted.Length < original.Length,
            $"expected the 32-bit chunk to be smaller: {converted.Length} vs {original.Length}");
    }

    [Fact]
    public void ConvertSizeT_PreservesTheDecompiledSourceExactly()
    {
        var original = LuaBytecodeService.Decompile(Compile());
        var converted = LuaBytecodeService.Decompile(Lua50ChunkRewriter.ConvertSizeT(Compile(), 4));

        Assert.True(original.Success);
        Assert.True(converted.Success);
        Assert.Equal(original.Source, converted.Source);
    }

    [Fact]
    public void ConvertSizeT_IsAnExactInverseAtTheSameWidth()
    {
        // Re-emitting at the same width must reproduce the input byte for byte; this is what proves
        // the parser and serialiser agree with the real chunk layout.
        var wide = Compile();
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(wide));
        Assert.Equal(wide, Lua50ChunkRewriter.ConvertSizeT(wide, 8, 8));

        var narrow = Lua50ChunkRewriter.ConvertSizeT(wide, 4);
        Assert.Equal(4, LuaBytecodeService.ReadSizeTSize(narrow));
        Assert.Equal(narrow, Lua50ChunkRewriter.ConvertSizeT(narrow, 4, 4));
    }

    [Fact]
    public void Compile_WithTargetSizeT_EmitsThatWidth()
    {
        Assert.Equal(4, LuaBytecodeService.ReadSizeTSize(Compile(4)));
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(Compile(8)));
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(Compile(null)));
    }

    [Fact]
    public void Compile_32BitOutputStillDecompilesToTheSameSource()
    {
        var wide = LuaBytecodeService.Decompile(Compile());
        var narrow = LuaBytecodeService.Decompile(Compile(4));

        Assert.True(narrow.Success);
        Assert.Equal(wide.Source, narrow.Source);
    }

    [Fact]
    public void ConvertSizeT_RejectsNonChunksAndBadWidths()
    {
        Assert.Throws<ArgumentException>(() =>
            Lua50ChunkRewriter.ConvertSizeT(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Lua50ChunkRewriter.ConvertSizeT(Compile(), 5));
    }

    [Fact]
    public void Document_TargetSizeT_DrivesTheWarningAndTheOutput()
    {
        var ds1Chunk = Lua50ChunkRewriter.ConvertSizeT(Compile(), 4);

        var doc = LuaScriptDocument.FromBytecode(ds1Chunk, "ds1.lua");
        Assert.Equal(4, doc.SizeTSize);

        // No preference: the original 32-bit layout is preserved, so no warning.
        Assert.Null(doc.TargetSizeT);
        Assert.False(doc.RecompileChangesChunkLayout);
        Assert.Equal(4, LuaBytecodeService.ReadSizeTSize(doc.Compile()));

        // Forced to 64-bit: warns, because that would no longer match the game.
        doc.TargetSizeT = 8;
        Assert.True(doc.RecompileChangesChunkLayout);
        Assert.Contains("size_t", doc.RecompileWarning);
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(doc.Compile()));
    }

    [Fact]
    public void Document_WithoutOriginalBytecode_FollowsThePreference()
    {
        var doc = LuaScriptDocument.CreateNewText();
        doc.Text = Source;

        // No preference and nothing to preserve, so the compiler's own width is used.
        Assert.Equal(0, doc.SizeTSize);
        Assert.Null(doc.TargetSizeT);
        Assert.False(doc.RecompileChangesChunkLayout);
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(doc.Compile()));

        // The "compile as 32-bit" preference is what makes new scripts PTDE compatible.
        doc.TargetSizeT = 4;
        Assert.Equal(4, LuaBytecodeService.ReadSizeTSize(doc.Compile()));
        Assert.False(doc.RecompileChangesChunkLayout);
    }
}
