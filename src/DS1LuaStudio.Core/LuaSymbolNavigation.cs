using System.Text.RegularExpressions;
using System.Buffers.Binary;
using LuaDecompilerCore;

namespace DS1LuaStudio.Core;

public static class LuaSymbolNavigation
{
    /// <summary>Checks Lua 5.0 SETGLOBAL instructions instead of confusing calls with definitions.</summary>
    public static bool HasGlobalDefinition(byte[] bytes, string symbol)
    {
        if (LuaBytecodeService.Detect(bytes) != LuaDialect.Lua50) return false;
        try
        {
            var lua = new LuaFile(new LuaDecompilerCore.Utilities.BinaryReaderEx(false, bytes));
            bool Visit(LuaFile.Function function)
            {
                for (int offset = 0; offset + 4 <= function.Bytecode.Length; offset += 4)
                {
                    var instruction = BinaryPrimitives.ReadUInt32LittleEndian(function.Bytecode.AsSpan(offset, 4));
                    int index = (int)((instruction >> 6) & 0x3FFFF);
                    if ((instruction & 0x3F) == 7 && index < function.Constants.Length && function.Constants[index].StringValue == symbol) return true;
                }
                return function.ChildFunctions.Any(Visit);
            }
            return Visit(lua.MainFunction);
        }
        catch { return false; }
    }
    /// <summary>Finds named functions and assignments, skipping comments and string literals.</summary>
    public static int FindDefinition(string source, string symbol)
    {
        if (!Regex.IsMatch(symbol, @"^[A-Za-z_][A-Za-z0-9_]*$")) return -1;
        var code = MaskLiterals(source);
        string name = Regex.Escape(symbol);
        var match = Regex.Match(code, $@"\bfunction\s+(?<name>{name})\s*\(|(?m:^\s*(?:local\s+)?(?<name>{name})\s*=(?!=))");
        return match.Success ? match.Groups["name"].Index : -1;
    }

    private static string MaskLiterals(string source)
    {
        // Keep offsets and line breaks intact so navigation points into the original text.
        return Regex.Replace(source, @"--\[(=*)\[[\s\S]*?\]\1\]|--[^\r\n]*|\[(=*)\[[\s\S]*?\]\2\]|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",
            m => new string(m.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()));
    }
}
