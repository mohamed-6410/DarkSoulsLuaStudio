using System.Text;
using SoulsFormats;

namespace DS1LuaStudio.Core;

/// <summary>
/// A Lua script being edited. It remembers where it came from so a recompile can be written
/// back to the right place - a standalone file, or an entry inside an <c>aistub.luabnd</c>.
/// </summary>
public sealed class LuaScriptDocument
{
    public string Title { get; private set; } = "untitled.lua";

    /// <summary>Standalone bytecode file this came from, if any.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Archive this was extracted from, if any.</summary>
    public LuabndArchive? Archive { get; private set; }

    /// <summary>Entry name inside <see cref="Archive"/>, if any.</summary>
    public string? EntryName { get; private set; }

    public LuaDialect Dialect { get; private set; } = LuaDialect.Lua50;

    public Encoding Encoding { get; private set; } = new UTF8Encoding(false);

    /// <summary>Bytecode this document was decompiled from, used for re-encoding round trips.</summary>
    public byte[]? OriginalBytecode { get; private set; }
    public string? DecompileError { get; private set; }
    public IReadOnlyList<int> WarningFunctions { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// The size_t width of the original chunk, or 0 when this script was not loaded from bytecode.
    /// Dark Souls PTDE uses 4 bytes.
    /// </summary>
    public int SizeTSize => OriginalBytecode is null ? 0 : LuaBytecodeService.ReadSizeTSize(OriginalBytecode);

    /// <summary>
    /// The size_t width to emit when this script is recompiled. Null keeps the compiler's own
    /// width. Set it to 4 to produce chunks Dark Souls PTDE can load.
    /// </summary>
    public int? TargetSizeT { get; set; }

    /// <summary>The size_t width that a recompile will actually write.</summary>
    public int EffectiveSizeT =>
        TargetSizeT ?? (SizeTSize != 0 ? SizeTSize : LuaBytecodeService.CompilerSizeTSize(Dialect));

    /// <summary>
    /// True when recompiling this script would change the chunk layout away from the original.
    /// </summary>
    public bool RecompileChangesChunkLayout =>
        OriginalBytecode is not null && EffectiveSizeT != 0 && EffectiveSizeT != SizeTSize;

    public string RecompileWarning => RecompileChangesChunkLayout
        ? $"Warning: this file uses a {SizeTSize * 8}-bit size_t but the output will " +
          $"use {EffectiveSizeT * 8}-bit. The bytecode may not load in game."
        : string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool IsDirty { get; set; }

    public bool IsFromArchive => Archive is not null && EntryName is not null;

    /// <summary>Status line friendly description of where this document will be saved.</summary>
    public string Origin
    {
        get
        {
            if (IsFromArchive) return $"{Path.GetFileName(Archive!.FilePath)} :: {EntryName}";
            if (FilePath is not null) return Path.GetFileName(FilePath);
            return "(unsaved)";
        }
    }

    /// <summary>Creates an empty, unsaved Lua 5.0 script ready to be typed into.</summary>
    public static LuaScriptDocument CreateNewText(string title = "untitled.lua") => new()
    {
        Title = title,
        Text = "-- new script\n",
        Dialect = LuaDialect.Lua50,
        Encoding = LuaBytecodeService.GetShiftJis(),
    };

    public static LuaScriptDocument FromBytecode(byte[] bytecode, string title, string? filePath = null,
        LuabndArchive? archive = null, string? entryName = null, DecompileSettings? settings = null)
    {
        var output = LuaBytecodeService.Decompile(bytecode, settings);
        var doc = new LuaScriptDocument
        {
            Title = title,
            FilePath = filePath,
            Archive = archive,
            EntryName = entryName,
            Dialect = output.Dialect,
            Encoding = output.Encoding,
            OriginalBytecode = bytecode,
            Text = output.Source,
            DecompileError = output.Error,
            WarningFunctions = output.WarningFunctions,
        };
        if (!output.Success)
            doc.Text = $"-- Decompilation failed: {output.Error}\n";
        return doc;
    }

    public static LuaScriptDocument FromTextFile(string path, Encoding? encoding = null)
    {
        encoding ??= DetectEncoding(path);
        var text = File.ReadAllText(path, encoding);
        return new LuaScriptDocument
        {
            Title = Path.GetFileName(path),
            FilePath = path,
            Text = text,
            Encoding = encoding,
        };
    }

    /// <summary>
    /// Decodes text bytes the way FromSoft tooling does: a BOM wins, otherwise Shift-JIS,
    /// otherwise UTF-8.
    /// </summary>
    public static Encoding DetectEncoding(string path)
    {
        LuaBytecodeService.EnsureEncodings();
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new UnicodeEncoding(false, true);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new UnicodeEncoding(true, true);

        // Strict UTF-8 validation; anything else is treated as Shift-JIS, which is what DS1 uses.
        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            strict.GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return LuaBytecodeService.GetShiftJis();
        }
    }

    /// <summary>
    /// Compiles the current text back into bytecode, honouring <see cref="TargetSizeT"/>. With no
    /// explicit preference a script opened from a chunk keeps that chunk's <c>size_t</c> width.
    /// </summary>
    public byte[] Compile()
    {
        if (DecompileError is not null)
            throw new InvalidOperationException("The original script failed to decompile. Export source or inspect disassembly before replacing bytecode: " + DecompileError);
        int? target = EffectiveSizeT == 0 ? null : EffectiveSizeT;
        return LuaBytecodeService.Compile(Text, Dialect, Encoding, target);
    }

    /// <summary>Compiles and writes a standalone bytecode file.</summary>
    public void SaveCompiled(string path)
    {
        var bytes = Compile();
        File.WriteAllBytes(path, bytes);
        FilePath = path;
        Archive = null;
        EntryName = null;
        OriginalBytecode = bytes;
        Title = Path.GetFileName(path);
        IsDirty = false;
    }

    /// <summary>
    /// Compiles and writes back into the archive entry this document came from. Because a modder
    /// expects "save" to mean "the file on disk is updated", the archive itself is rewritten too
    /// unless <paramref name="writeArchiveToDisk"/> is turned off (for batch edits that are saved later).
    /// </summary>
    public void SaveCompiledToArchive(bool writeArchiveToDisk = true)
    {
        if (Archive is null || EntryName is null)
            throw new InvalidOperationException("This document is not backed by an archive entry.");

        var bytes = Compile();
        Archive.ReplaceEntry(EntryName, bytes);
        OriginalBytecode = bytes;
        IsDirty = false;

        if (writeArchiveToDisk && !string.IsNullOrEmpty(Archive.FilePath))
            Archive.Save();
    }

    public void SaveSource(string path)
    {
        File.WriteAllText(path, Text, Encoding);
        FilePath = path;
        Title = Path.GetFileName(path);
        IsDirty = false;
    }
}

/// <summary>
/// The list of global variable names Lua scripts may use (<c>*.luagnl</c>).
/// </summary>
public sealed class LuaGnlDocument
{
    private DCX.CompressionInfo _compression = new DCX.NoCompressionInfo();
    public LuabndArchive? Archive { get; private set; }
    public string? EntryName { get; private set; }

    public static LuaGnlDocument CreateNew(GameMode mode = GameMode.DS1) => new() { LongFormat = mode == GameMode.DS3 };

    public static LuaGnlDocument FromArchive(LuabndArchive archive, string entryName)
    {
        var doc = new LuaGnlDocument();
        using var stream = new MemoryStream(archive.GetBytes(entryName) ?? throw new ArgumentException("Missing archive entry."));
        doc.Load(stream);
        doc.Archive = archive;
        doc.EntryName = entryName;
        return doc;
    }
    public List<string> Globals { get; } = new();

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    public bool BigEndian { get; private set; }

    public bool LongFormat { get; private set; }

    public void Load(string path)
    {
        LuaBytecodeService.EnsureEncodings();
        using var stream = File.OpenRead(path);
        Load(stream, path);
    }

    public void Load(Stream stream, string? path = null)
    {
        LuaBytecodeService.EnsureEncodings();
        var gnl = SoulsFile<LUAGNL>.Read(stream);

        Globals.Clear();
        Globals.AddRange(gnl.Globals);
        BigEndian = gnl.BigEndian;
        LongFormat = gnl.LongFormat;
        _compression = gnl.Compression;
        FilePath = path;
        IsDirty = false;
    }

    public void Save(string path)
    {
        var gnl = BuildSource();
        gnl.Write(path);
        FilePath = path;
        Archive = null;
        EntryName = null;
        IsDirty = false;
    }

    private LUAGNL BuildSource() => new(BigEndian, LongFormat) { Globals = Globals.ToList(), Compression = _compression };
    public byte[] ToBytes() => BuildSource().Write();
    public void SaveToArchive()
    {
        if (Archive is null || EntryName is null) throw new InvalidOperationException("No archive origin.");
        Archive.ReplaceEntry(EntryName, ToBytes());
        Archive.Save();
        IsDirty = false;
    }
}
