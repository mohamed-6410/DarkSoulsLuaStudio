using System.Text;
using LuaCompiler;
using LuaCompiler.Compilers;
using LuaDecompilerCore;
using LuaDecompilerCore.IR;
using LuaDecompilerCore.LanguageDecompilers;
using LuaDecompilerCore.Utilities;

namespace DS1LuaStudio.Core;

/// <summary>
/// Bytecode dialects understood by DSLuaDecompiler.
/// </summary>
public enum LuaDialect
{
    /// <summary>Unknown / not recognised.</summary>
    Unknown = 0,

    /// <summary>
    /// Lua 5.0. This is what Dark Souls Prepare to Die Edition uses for its AI scripts
    /// (<c>aistub.luabnd</c>).
    /// </summary>
    Lua50 = 1,

    /// <summary>Havok Script (Lua 5.1 based). Used for character control logic in later FromSoft titles.</summary>
    HavokScript = 2,

    /// <summary>Lua 5.3 as used by Super Smash Bros. Ultimate UI scripts.</summary>
    Lua53Smash = 3,
}

public sealed record DecompileSettings
{
    public bool CatchPassExceptions { get; init; } = true;
    public bool OutputDebugComments { get; init; }
    public bool IgnoreDebugInfo { get; init; }
    public IReadOnlySet<int>? ExcludedFunctionIds { get; init; }
}

public sealed record DecompileOutput
{
    public required string Source { get; init; }
    public required LuaDialect Dialect { get; init; }
    public required Encoding Encoding { get; init; }
    public IReadOnlyList<int> WarningFunctions { get; init; } = Array.Empty<int>();

    /// <summary>Per-function metadata, so callers can give generated names a meaning.</summary>
    public IReadOnlyList<DecompiledFunction> Functions { get; init; } = Array.Empty<DecompiledFunction>();

    public string? Error { get; init; }
    public bool Success => Error is null;
}

/// <summary>What the decompiler knows about one function, keyed by its internal id.</summary>
public sealed record DecompiledFunction(int FunctionId, string? Name, int ParameterCount);

/// <summary>
/// Wraps DSLuaDecompiler / LuaCompiler so the UI never has to touch the decompiler directly.
/// </summary>
public static class LuaBytecodeService
{
    private static bool _codepagesRegistered;

    static LuaBytecodeService() => EnsureEncodings();

    public static void EnsureEncodings()
    {
        if (_codepagesRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _codepagesRegistered = true;
    }

    /// <summary>
    /// The dialect a script's source should be written/read with. Matches DSLuaDecompiler's own choice.
    /// </summary>
    public static Encoding EncodingFor(LuaDialect dialect) => dialect switch
    {
        LuaDialect.Lua50 => GetShiftJis(),
        LuaDialect.HavokScript => new UTF8Encoding(false),
        LuaDialect.Lua53Smash => new UTF8Encoding(false),
        _ => new UTF8Encoding(false),
    };

    public static Encoding GetShiftJis()
    {
        EnsureEncodings();
        return Encoding.GetEncoding("shift_jis");
    }

    /// <summary>
    /// Sniffs the bytecode dialect from the raw header. Deliberately strict and non-throwing:
    /// it validates the Lua signature and version byte so that short or arbitrary files inside a
    /// binder are never mistaken for scripts.
    /// </summary>
    public static LuaDialect Detect(byte[] data)
    {
        // ESC "Lua" signature, version byte, then at least the header fields that follow.
        if (data.Length < 12) return LuaDialect.Unknown;
        if (data[0] != 0x1B || data[1] != 0x4C || data[2] != 0x75 || data[3] != 0x61)
            return LuaDialect.Unknown;

        return data[4] switch
        {
            0x50 => LuaDialect.Lua50,
            // Havok Script is Lua 5.1 with the 0x0E marker right after the version byte.
            0x51 when data[5] == 0x0E => LuaDialect.HavokScript,
            0x53 => LuaDialect.Lua53Smash,
            _ => LuaDialect.Unknown,
        };
    }

    public static bool LooksLikeBytecode(byte[] data) => Detect(data) != LuaDialect.Unknown;

    /// <summary>
    /// Reads the chunk header's size_t width in bytes. Dark Souls PTDE was built with a 32-bit
    /// size_t; most modern builds (including the bundled Lua 5.0 compiler) use 64-bit. Returns 0
    /// when the data is not a recognised chunk.
    /// </summary>
    public static int ReadSizeTSize(byte[] data)
    {
        if (data.Length < 8) return 0;
        if (data[0] != 0x1B || data[1] != 0x4C || data[2] != 0x75 || data[3] != 0x61) return 0;

        int widthOffset = data[4] switch { 0x50 => 7, 0x51 => 8, 0x53 => 13, _ => -1 };
        if (widthOffset < 0) return 0;
        if (data.Length <= widthOffset) return 0;
        return data[widthOffset] switch
        {
            4 => 4,
            8 => 8,
            _ => 0,
        };
    }

    /// <summary>The size_t width the compiler behind <see cref="ICompiler"/> will emit.</summary>
    public static int CompilerSizeTSize(LuaDialect dialect) => dialect == LuaDialect.Lua50
        // Verified against lua502.dll: it writes 64-bit string lengths.
        ? 8
        : 0;

    private static string DialectName(LuaDialect dialect) => dialect switch
    {
        LuaDialect.Lua50 => "Lua 5.0",
        LuaDialect.HavokScript => "Havok Script",
        LuaDialect.Lua53Smash => "Lua 5.3",
        _ => "Lua",
    };

    public static DecompileOutput Decompile(byte[] bytecode, DecompileSettings? settings = null)
    {
        settings ??= new DecompileSettings();
        EnsureEncodings();

        var dialect = Detect(bytecode);
        if (dialect == LuaDialect.Unknown)
        {
            return new DecompileOutput
            {
                Source = string.Empty,
                Dialect = LuaDialect.Unknown,
                Encoding = new UTF8Encoding(false),
                Error = "Not a recognised Lua bytecode file (missing the Lua signature or an unknown version).",
            };
        }

        var encoding = EncodingFor(dialect);

        LuaFile lua;
        try
        {
            lua = new LuaFile(new BinaryReaderEx(false, new MemoryStream(bytecode, writable: false)));
        }
        catch (Exception ex)
        {
            return new DecompileOutput
            {
                Source = string.Empty,
                Dialect = dialect,
                Encoding = encoding,
                Error = $"Could not parse the {DialectName(dialect)} chunk: {ex.Message}",
            };
        }

        ILanguageDecompiler languageDecompiler = dialect switch
        {
            LuaDialect.Lua50 => new Lua50Decompiler(),
            LuaDialect.HavokScript => new HksDecompiler(),
            LuaDialect.Lua53Smash => new Lua53Decompiler(),
            _ => throw new InvalidOperationException(),
        };

        var options = new DecompilationOptions
        {
            CatchPassExceptions = settings.CatchPassExceptions,
            OutputDebugComments = settings.OutputDebugComments,
            IgnoreDebugInfo = settings.IgnoreDebugInfo,
        };
        if (settings.ExcludedFunctionIds is { Count: > 0 })
            options.ExcludedFunctionIds = settings.ExcludedFunctionIds;

        var decompiler = new LuaDecompiler(options);
        var main = new Function(lua.MainFunction.FunctionId);

        DecompilationResult result;
        try
        {
            result = decompiler.DecompileLuaFunction(languageDecompiler, main, lua.MainFunction);
        }
        catch (Exception ex)
        {
            return new DecompileOutput
            {
                Source = string.Empty,
                Dialect = dialect,
                Encoding = encoding,
                Error = ex.Message,
            };
        }

        return new DecompileOutput
        {
            Source = result.DecompiledSource ?? string.Empty,
            Dialect = dialect,
            Encoding = encoding,
            WarningFunctions = result.FunctionsWithWarnings ?? Array.Empty<int>(),
            Functions = CollectFunctions(lua.MainFunction),
            Error = result.ErrorMessage,
        };
    }

    /// <summary>Flattens the function tree so callers can map generated names back to real ones.</summary>
    private static List<DecompiledFunction> CollectFunctions(LuaFile.Function root)
    {
        var all = new List<DecompiledFunction>();
        void Visit(LuaFile.Function function)
        {
            all.Add(new DecompiledFunction(
                function.FunctionId,
                function.Name,
                (int)function.NumParams));

            foreach (var child in function.ChildFunctions) Visit(child);
        }

        Visit(root);
        return all;
    }

    /// <summary>
    /// The disassembler output DSLuaDecompiler can produce for debugging purposes.
    /// </summary>
    public static string? Disassemble(byte[] bytecode)
    {
        try
        {
            var lua = new LuaFile(new BinaryReaderEx(false, new MemoryStream(bytecode, writable: false)));
            ILanguageDecompiler languageDecompiler = lua.Version switch
            {
                LuaFile.LuaVersion.Lua50 => new Lua50Decompiler(),
                LuaFile.LuaVersion.Lua51Hks => new HksDecompiler(),
                LuaFile.LuaVersion.Lua53Smash => new Lua53Decompiler(),
                _ => throw new InvalidOperationException(),
            };
            return new LuaDecompiler(new DecompilationOptions()).DisassembleLuaFunction(languageDecompiler, lua.MainFunction);
        }
        catch (Exception ex)
        {
            return $"Disassembly failed: {ex.Message}";
        }
    }

    /// <summary>
    /// True when the native library required to build this dialect is present. The Lua 5.0
    /// compiler ships with the project; the Havok Script compiler needs an external DLL.
    /// </summary>
    private const string HavokCompilerLibrary = "LuaNative/HavokScript_FinalRelease.dll";

    /// <summary>
    /// The Havok Script compiler is a native DLL that is not redistributable, so it is only usable
    /// when the modder has dropped it in next to the executable.
    /// </summary>
    private static bool HasHavokCompilerNativeLibrary()
    {
        try
        {
            var relative = HavokCompilerLibrary.Replace('/', Path.DirectorySeparatorChar);
            foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                if (File.Exists(Path.Combine(dir, relative))) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsCompilerAvailable(LuaDialect dialect) => dialect switch
    {
        LuaDialect.Lua50 => true,
        LuaDialect.HavokScript => HasHavokCompilerNativeLibrary(),
        _ => false,
    };

    public static string? CompilerUnavailableReason(LuaDialect dialect) => dialect switch
    {
        LuaDialect.Lua50 => null,
        LuaDialect.HavokScript =>
            "Compiling Havok Script requires 'HavokScript_FinalRelease.dll' (from Civilization VI) in the " +
            "LuaNative folder next to the executable. Decompiling Havok Script works without it.",
        LuaDialect.Lua53Smash => "Lua 5.3 is decompile-only in DSLuaDecompiler.",
        _ => "No compiler for this dialect.",
    };

    /// <summary>
    /// Compiles Lua source to bytecode.
    /// <para>
    /// <paramref name="targetSizeT"/> requests a specific <c>size_t</c> width. The bundled Lua 5.0
    /// compiler always emits 64-bit lengths, so pass 4 to produce chunks Dark Souls PTDE can load.
    /// </para>
    /// </summary>
    public static byte[] Compile(string source, LuaDialect dialect, Encoding? encoding = null, int? targetSizeT = null)
    {
        EnsureEncodings();
        encoding ??= EncodingFor(dialect);

        ICompiler compiler = dialect switch
        {
            LuaDialect.Lua50 => new Lua50Compiler(),
            LuaDialect.HavokScript => new LuaHavokScriptCompiler(),
            _ => throw new NotSupportedException(CompilerUnavailableReason(dialect)),
        };

        byte[] result;
        try
        {
            result = compiler.CompileSource(source, encoding);
        }
        catch (CompileException ex)
        {
            throw new LuaCompileException(ex.Message, ex);
        }
        catch (DllNotFoundException ex)
        {
            throw new LuaCompileException(CompilerUnavailableReason(dialect) ?? ex.Message, ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new LuaCompileException(
                "The native compiler library was found but is not a compatible build: " + ex.Message, ex);
        }

        if (targetSizeT.HasValue && dialect == LuaDialect.Lua50)
            result = Lua50ChunkRewriter.ConvertSizeT(result, targetSizeT.Value);

        return result;
    }
}

/// <summary>Raised when Lua source cannot be turned into bytecode.</summary>
public sealed class LuaCompileException : Exception
{
    public LuaCompileException(string message, Exception inner) : base(message, inner) { }
}
