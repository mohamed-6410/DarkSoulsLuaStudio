using SoulsFormats;

namespace DS1LuaStudio.Core;

public sealed record ArchiveEntry(string Name, byte[] Bytes, bool LooksLikeLua, bool LooksLikeLuaInfo)
{
    public int Size => Bytes.Length;
    public bool LooksLikeLuaGnl => Name.EndsWith(".luagnl", StringComparison.OrdinalIgnoreCase);
    public bool CanOpen => LooksLikeLua || LooksLikeLuaInfo || LooksLikeLuaGnl;
    public string Kind => LooksLikeLua ? "Lua" : LooksLikeLuaInfo ? "Goal table" : LooksLikeLuaGnl ? "Globals" : "Data";
}

/// <summary>
/// A FromSoft binder archive (<c>aistub.luabnd</c> and friends). DS1 PTDE uses BND3, later
/// titles use BND4; both are handled, with or without DCX compression.
/// </summary>
public sealed class LuabndArchive
{
    private readonly BND3? _bnd3;
    private readonly BND4? _bnd4;
    private readonly List<BinderFile> _files;

    public string FilePath { get; private set; }

    public bool IsBnd3 => _bnd3 is not null;

    public bool IsDirty { get; private set; }

    public IReadOnlyList<ArchiveEntry> Entries => _files
        .Select(f => new ArchiveEntry(
            f.Name ?? "(unnamed)",
            f.Bytes,
            LuaBytecodeService.LooksLikeBytecode(f.Bytes),
            IsLuaInfo(f.Bytes)))
        .ToList();

    private LuabndArchive(BND3? bnd3, BND4? bnd4, string? path)
    {
        _bnd3 = bnd3;
        _bnd4 = bnd4;
        _files = (bnd3?.Files ?? bnd4?.Files ?? new List<BinderFile>())!;
        FilePath = path ?? string.Empty;
    }

    private static bool IsLuaInfo(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == (byte)'L' && bytes[1] == (byte)'U' &&
                           bytes[2] == (byte)'A' && bytes[3] == (byte)'I';

    /// <summary>Opens a binder, returning null when the file is not one.</summary>
    public static LuabndArchive? TryOpen(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch { return null; }

        if (SoulsFile<BND3>.Is(bytes))
            return new LuabndArchive(BND3.Read(bytes), null, path);
        if (SoulsFile<BND4>.Is(bytes))
            return new LuabndArchive(null, BND4.Read(bytes), path);

        return null;
    }

    public byte[]? GetBytes(string entryName)
    {
        var file = _files.FirstOrDefault(f => string.Equals(f.Name, entryName, StringComparison.OrdinalIgnoreCase));
        return file?.Bytes;
    }

    /// <summary>Replaces an entry's contents in place, preserving its id, flags and compression.</summary>
    public void ReplaceEntry(string entryName, byte[] bytes)
    {
        var file = _files.FirstOrDefault(f => string.Equals(f.Name, entryName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"No entry named '{entryName}' in the archive.", nameof(entryName));

        file.Bytes = bytes;
        IsDirty = true;
    }

    /// <summary>Adds a new entry to the archive.</summary>
    public void AddEntry(string entryName, byte[] bytes)
    {
        // Flag1 is what FromSoft's own tooling uses for ordinary named entries.
        var file = new BinderFile(Binder.FileFlags.Flag1, entryName, bytes);
        _files.Add(file);
        IsDirty = true;
    }

    public void RemoveEntry(string entryName)
    {
        var file = _files.FirstOrDefault(f => string.Equals(f.Name, entryName, StringComparison.OrdinalIgnoreCase));
        if (file is null) return;
        _files.Remove(file);
        IsDirty = true;
    }

    public void Save(string path)
    {
        if (_bnd3 is not null) _bnd3.Write(path);
        else if (_bnd4 is not null) _bnd4.Write(path);
        else throw new InvalidOperationException("Archive was not loaded from a readable binder.");

        FilePath = path;
        IsDirty = false;
    }

    public void Save() => Save(FilePath);
}
