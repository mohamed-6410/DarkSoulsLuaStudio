namespace DS1LuaStudio.Core;

public enum ScriptFileKind
{
    Unknown,
    LuaInfo,
    LuaGnl,
    LuaSource,
    LuaBytecode,
    LuabndArchive,
}

/// <summary>Decides how a file should be opened, and opens it into the right document type.</summary>
public static class ScriptFileOpener
{
    /// <summary>
    /// Decides how a file should be opened, and opens it into the right document type.
    /// </summary>
    public static ScriptFileKind Classify(string path)
    {
        // Read the head defensively: the extension switch below must still work for paths that
        // cannot be read (or do not exist, when this is called on a bare file name).
        byte[] head = TryReadHead(path, 16);

        // Content first: Dark Souls PTDE ships its AI scripts as *compiled* Lua 5.0 bytecode with a
        // .lua extension, so the extension cannot be trusted on its own.
        if (HasLuaSignature(head))
            return ScriptFileKind.LuaBytecode;

        // LUAINFO carries the "LUAI" magic.
        if (head.Length >= 4 && head[0] == (byte)'L' && head[1] == (byte)'U' &&
                              head[2] == (byte)'A' && head[3] == (byte)'I')
            return ScriptFileKind.LuaInfo;

        string logicalPath = path.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
        switch (Path.GetExtension(logicalPath).ToLowerInvariant())
        {
            case ".luainfo": return ScriptFileKind.LuaInfo;
            case ".luagnl": return ScriptFileKind.LuaGnl;
            case ".lua": return ScriptFileKind.LuaSource;
            case ".luabnd":
            case ".bnd":
                return ScriptFileKind.LuabndArchive;
        }

        // Last resort for extension-less binder dumps.
        try
        {
            if (SoulsFormats.SoulsFile<SoulsFormats.BND3>.Is(path)) return ScriptFileKind.LuabndArchive;
            if (SoulsFormats.SoulsFile<SoulsFormats.BND4>.Is(path)) return ScriptFileKind.LuabndArchive;
        }
        catch
        {
            // not a binder, or unreadable
        }

        return ScriptFileKind.Unknown;
    }

    /// <summary>
    /// True for the 4-byte Lua signature "\x1bLua" plus a known version byte. Used to spot bytecode
    /// that is wearing a source file extension.
    /// </summary>
    private static bool HasLuaSignature(byte[] head)
    {
        if (head.Length < 5) return false;
        if (head[0] != 0x1B || head[1] != 0x4C || head[2] != 0x75 || head[3] != 0x61) return false;

        return head[4] switch
        {
            0x50 => true,                                  // Lua 5.0
            0x51 => head.Length > 5 && head[5] == 0x0E,    // Havok Script
            0x53 => true,                                  // Lua 5.3
            _ => false,
        };
    }

    private static byte[] TryReadHead(string path, int count)
    {
        try { return ReadHead(path, count); }
        catch { return Array.Empty<byte>(); }
    }

    private static byte[] ReadHead(string path, int count)
    {
        using var fs = File.OpenRead(path);
        var buffer = new byte[count];
        int read = fs.Read(buffer, 0, count);
        if (read == count) return buffer;
        var trimmed = new byte[read];
        Array.Copy(buffer, trimmed, read);
        return trimmed;
    }

    /// <summary>
    /// Opens a bytecode file into an editable script document.
    /// </summary>
    public static LuaScriptDocument OpenBytecode(string path, DecompileSettings? settings = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return LuaScriptDocument.FromBytecode(bytes, Path.GetFileName(path), path, settings: settings);
    }

    /// <summary>Opens a bytecode archive entry into an editable script document.</summary>
    public static LuaScriptDocument OpenArchiveEntry(LuabndArchive archive, string entryName,
        DecompileSettings? settings = null)
    {
        var bytes = archive.GetBytes(entryName)
                    ?? throw new ArgumentException($"No entry named '{entryName}'.", nameof(entryName));
        return LuaScriptDocument.FromBytecode(bytes, Path.GetFileName(entryName), archive: archive, entryName: entryName,
            settings: settings);
    }

    /// <summary>
    /// Bulk-extracts every decompilable script in an archive as <c>.lua</c> files into a folder.
    /// This is the "decompile the whole aistub" workflow.
    /// </summary>
    public static IReadOnlyList<string> ExtractAllScripts(LuabndArchive archive, string outputDirectory,
        DecompileSettings? settings = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        foreach (var entry in archive.Entries)
        {
            if (!entry.LooksLikeLua) continue;

            string name = SanitizeFileName(entry.Name);
            string path = Path.Combine(outputDirectory, name + ".lua");

            var doc = LuaScriptDocument.FromBytecode(entry.Bytes, entry.Name, archive: archive,
                entryName: entry.Name, settings: settings);
            if (doc.DecompileError is not null)
                throw new InvalidOperationException($"Could not decompile {entry.Name}: {doc.DecompileError}");

            File.WriteAllText(path, doc.Text, doc.Encoding);
            written.Add(path);
        }

        return written;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
