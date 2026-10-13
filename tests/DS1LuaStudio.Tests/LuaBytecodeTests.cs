using System.Text;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.Tests;

/// <summary>
/// DS1 Prepare to Die Edition AI scripts are Lua 5.0 bytecode, which is the one dialect whose
/// compiler ships with DSLuaDecompiler, so the full edit -> recompile loop is testable here.
/// </summary>
public class LuaBytecodeTests
{
    private const string SampleSource = """
        local function add(a, b)
            local t = { a, b, 3 }
            for i = 1, 10 do
                t[i] = t[i] * 2
            end
            if a > b then
                return a + b, "big"
            else
                return b - a
            end
        end
        for k, v in pairs({ x = 1, y = 2 }) do
            add(k, v)
        end
        """;

    private static byte[] Compile(string source) =>
        LuaBytecodeService.Compile(source, LuaDialect.Lua50, Encoding.UTF8);

    [Fact]
    public void Lua50Compiler_IsAvailable()
    {
        Assert.True(LuaBytecodeService.IsCompilerAvailable(LuaDialect.Lua50));
        Assert.Null(LuaBytecodeService.CompilerUnavailableReason(LuaDialect.Lua50));
    }

    [Fact]
    public void Detect_IdentifiesLua50()
    {
        var bytecode = Compile(SampleSource);
        Assert.Equal(LuaDialect.Lua50, LuaBytecodeService.Detect(bytecode));
        Assert.True(LuaBytecodeService.LooksLikeBytecode(bytecode));
    }

    [Fact]
    public void Detect_ReturnsUnknownForGarbage()
    {
        Assert.Equal(LuaDialect.Unknown, LuaBytecodeService.Detect(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.False(LuaBytecodeService.LooksLikeBytecode(new byte[] { 0 }));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x1B, 0x4C, 0x75, 0x61, 0x99, 0x01, 0x04, 0x08, 0x04, 0x08, 0x08, 0x00 })]
    public void Detect_RejectsNonLuaPayloads(byte[] data)
    {
        // Important for binder archives: arbitrary entries must never be treated as scripts.
        Assert.Equal(LuaDialect.Unknown, LuaBytecodeService.Detect(data));
        Assert.False(LuaBytecodeService.LooksLikeBytecode(data));
    }

    [Fact]
    public void Detect_IdentifiesHavokScriptByMarkerByte()
    {
        byte[] hks = { 0x1B, 0x4C, 0x75, 0x61, 0x51, 0x0E, 0x01, 0x04, 0x08, 0x04, 0x08, 0x00 };
        Assert.Equal(LuaDialect.HavokScript, LuaBytecodeService.Detect(hks));

        // Same signature but the marker byte is missing, so it is not treated as Havok Script.
        byte[] notHks = { 0x1B, 0x4C, 0x75, 0x61, 0x51, 0x0F, 0x01, 0x04, 0x08, 0x04, 0x08, 0x00 };
        Assert.Equal(LuaDialect.Unknown, LuaBytecodeService.Detect(notHks));
    }

    [Fact]
    public void Decompile_SucceedsAndKeepsControlFlow()
    {
        var bytecode = Compile(SampleSource);
        var result = LuaBytecodeService.Decompile(bytecode);

        Assert.True(result.Success, result.Error);
        Assert.Equal(LuaDialect.Lua50, result.Dialect);
        Assert.Contains("function", result.Source);
        Assert.Contains("for", result.Source);
        Assert.Contains("if", result.Source);
        Assert.Contains("return", result.Source);
    }

    [Fact]
    public void Decompile_UsesShiftJisForLua50()
    {
        var result = LuaBytecodeService.Decompile(Compile(SampleSource));
        Assert.Equal("shift_jis", result.Encoding.WebName);
    }

    [Fact]
    public void DecompileOutput_IsDeterministic()
    {
        var bytecode = Compile(SampleSource);
        Assert.Equal(
            LuaBytecodeService.Decompile(bytecode).Source,
            LuaBytecodeService.Decompile(bytecode).Source);
    }

    [Fact]
    public void ReadSizeTSize_ReportsTheHeaderWidth()
    {
        // The bundled Lua 5.0 compiler emits 64-bit string lengths.
        var mine = Compile(SampleSource);
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(mine));
        Assert.Equal(8, LuaBytecodeService.CompilerSizeTSize(LuaDialect.Lua50));
    }

    [Fact]
    public void ReadSizeTSize_ReturnsZeroForNonChunks()
    {
        Assert.Equal(0, LuaBytecodeService.ReadSizeTSize(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        Assert.Equal(0, LuaBytecodeService.ReadSizeTSize(new byte[] { 0x1B, 0x4C }));
    }

    [Fact]
    public void RecompileWarning_IsRaisedOnlyWhenTheLayoutWouldChange()
    {
        // A script that came from the bundled compiler keeps its 64-bit layout: no warning.
        var doc = LuaScriptDocument.FromBytecode(Compile(SampleSource), "test.lua");
        Assert.Equal(8, doc.SizeTSize);
        Assert.False(doc.RecompileChangesChunkLayout);
        Assert.Equal(string.Empty, doc.RecompileWarning);
        Assert.Equal(8, LuaBytecodeService.ReadSizeTSize(doc.Compile()));

        // A Dark Souls PTDE chunk (32-bit size_t) is preserved by default.
        var ds1 = SimulateDs1Chunk(Compile(SampleSource));
        var ds1Doc = LuaScriptDocument.FromBytecode(ds1, "ds1.lua");
        Assert.Equal(4, ds1Doc.SizeTSize);
        Assert.False(ds1Doc.RecompileChangesChunkLayout);

        // Forcing 64-bit on it would no longer match the game, so that does warn.
        ds1Doc.TargetSizeT = 8;
        Assert.True(ds1Doc.RecompileChangesChunkLayout);
        Assert.Contains("size_t", ds1Doc.RecompileWarning);
    }

    /// <summary>Rewrites a chunk's size_t header field from 8 to 4, as Dark Souls PTDE does.</summary>
    private static byte[] SimulateDs1Chunk(byte[] bytecode)
    {
        var copy = (byte[])bytecode.Clone();
        copy[7] = 4;
        return copy;
    }

    [Fact]
    public void Decompile_RejectsNonBytecode()
    {
        var result = LuaBytecodeService.Decompile(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02 });
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void DecompileThenRecompile_ProducesIdenticalBytecode()
    {
        var original = Compile(SampleSource);
        var decompiled = LuaBytecodeService.Decompile(original).Source;
        var recompiled = LuaBytecodeService.Compile(decompiled, LuaDialect.Lua50, Encoding.UTF8);

        Assert.Equal(Original.MainBytecode(original), Original.MainBytecode(recompiled));
    }

    [Fact]
    public void Compile_ReportsSyntaxErrors()
    {
        var ex = Assert.Throws<LuaCompileException>(() => Compile("this is not lua ==="));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Lua53_IsDecompileOnly()
    {
        Assert.False(LuaBytecodeService.IsCompilerAvailable(LuaDialect.Lua53Smash));
        Assert.NotNull(LuaBytecodeService.CompilerUnavailableReason(LuaDialect.Lua53Smash));
    }

    [Fact]
    public void FromBytecode_CarriesOriginAndEncoding()
    {
        var doc = LuaScriptDocument.FromBytecode(Compile(SampleSource), "test.lua");
        Assert.Equal("test.lua", doc.Title);
        Assert.Equal(LuaDialect.Lua50, doc.Dialect);
        Assert.Contains("function", doc.Text);
    }

    [Fact]
    public void FromTextFile_DetectsUtf8WithoutBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.lua");
        try
        {
            File.WriteAllText(path, SampleSource, new UTF8Encoding(false));
            var doc = LuaScriptDocument.FromTextFile(path);
            Assert.Equal("utf-8", doc.Encoding.WebName);
            Assert.Contains("local function add", doc.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromTextFile_DetectsShiftJis()
    {
        var sjis = LuaBytecodeService.GetShiftJis();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.lua");
        try
        {
            // A byte sequence that is invalid UTF-8 but valid Shift-JIS.
            File.WriteAllBytes(path, sjis.GetBytes("-- 阻力テスト\nlocal x = 1\n"));
            var doc = LuaScriptDocument.FromTextFile(path);
            Assert.Equal("shift_jis", doc.Encoding.WebName);
            Assert.Contains("local x = 1", doc.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Compares the parsed instruction stream of the main chunk of two bytecode files.</summary>
    private static class Original
    {
        public static byte[] MainBytecode(byte[] data)
        {
            var br = new LuaDecompilerCore.Utilities.BinaryReaderEx(false, new MemoryStream(data, writable: false));
            return new LuaDecompilerCore.LuaFile(br).MainFunction.Bytecode;
        }
    }
}
