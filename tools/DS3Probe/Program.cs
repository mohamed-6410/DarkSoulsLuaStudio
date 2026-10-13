using DS1LuaStudio.Core;
using SoulsFormats;
using LuaDecompilerCore;
using System.Text.Json;
using System.Text.RegularExpressions;
LuaBytecodeService.EnsureEncodings();
var symbols = new SortedSet<string>(StringComparer.Ordinal);
int scripts = 0, infos = 0, gnls = 0, failed = 0, warned = 0, compileFailed = 0;
bool verify = args.Contains("--verify");
foreach (var path in Directory.GetFiles("DS3-Luascripts/script", "*.dcx"))
{
    var archive = LuabndArchive.TryOpen(path)!;
    foreach (var entry in archive.Entries)
    {
        if (entry.LooksLikeLuaInfo)
        {
            var info = LUAINFO.Read(entry.Bytes); if (!info.LongFormat) throw new Exception("Short info");
            using var stream = new MemoryStream(entry.Bytes); var doc = new LuaInfoDocument(); doc.Load(stream);
            if (!entry.Bytes.SequenceEqual(doc.ToBytes())) throw new Exception("INFO roundtrip mismatch: " + entry.Name);
            foreach(var g in info.Goals) symbols.Add(g.Name); infos++;
        }
        if (entry.LooksLikeLuaGnl)
        {
            var gnl = LUAGNL.Read(entry.Bytes); if (!gnl.LongFormat) throw new Exception("Short gnl");
            using var stream = new MemoryStream(entry.Bytes); var doc = new LuaGnlDocument(); doc.Load(stream);
            if (!entry.Bytes.SequenceEqual(doc.ToBytes())) throw new Exception("GNL roundtrip mismatch: " + entry.Name);
            symbols.UnionWith(gnl.Globals); gnls++;
        }
        if (entry.LooksLikeLua)
        {
            if (LuaBytecodeService.ReadSizeTSize(entry.Bytes) != 8) throw new Exception("Short Lua");
            if (!entry.Bytes.SequenceEqual(Lua50ChunkRewriter.ConvertSizeT(entry.Bytes, 8))) throw new Exception("Lua roundtrip mismatch");
            var lua = new LuaFile(new LuaDecompilerCore.Utilities.BinaryReaderEx(false, new MemoryStream(entry.Bytes)));
            void Visit(LuaFile.Function f) { foreach (var c in f.Constants) if(c.StringValue is {} s && Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_]*$")) symbols.Add(s); foreach (var child in f.ChildFunctions) Visit(child); }
            Visit(lua.MainFunction); scripts++;
            if (verify)
            {
                var result = LuaBytecodeService.Decompile(entry.Bytes);
                if (!result.Success) { failed++; Console.WriteLine($"DECOMPILE FAIL {Path.GetFileName(path)} {Path.GetFileName(entry.Name)}: {result.Error}"); }
                if (result.WarningFunctions.Count > 0) { warned++; Console.WriteLine($"WARN {Path.GetFileName(path)} {Path.GetFileName(entry.Name)} functions={result.WarningFunctions.Count}"); }
                if (result.Success)
                {
                    try { var compiled = LuaBytecodeService.Compile(result.Source, result.Dialect, result.Encoding, 8); if (!LuaBytecodeService.Decompile(compiled).Success) throw new Exception("Recompiled chunk cannot decompile"); }
                    catch (Exception ex) { compileFailed++; Console.WriteLine($"COMPILE FAIL {Path.GetFileName(path)} {Path.GetFileName(entry.Name)}: {ex.Message}"); }
                }
            }
        }
    }
    Console.WriteLine($"Checked {Path.GetFileName(path)}");
}
if (!verify) File.WriteAllText("src/DS1LuaStudio.Core/Data/DS3Symbols.json", JsonSerializer.Serialize(new { provenance = "Identifiers observed in the supplied DS3 LUAGNL tables, LUAINFO names and Lua 5.0 string constants. No inferred parameter signatures.", symbols }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"scripts={scripts} infos={infos} gnls={gnls} symbols={symbols.Count} decompileFailures={failed} warnedScripts={warned} compileFailures={compileFailed}");
