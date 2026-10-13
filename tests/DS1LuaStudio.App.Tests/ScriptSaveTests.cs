using DS1LuaStudio.App.ViewModels;
using DS1LuaStudio.Core;
using Xunit;

namespace DS1LuaStudio.App.Tests;

/// <summary>
/// Saving a .lua source file used to overwrite it with compiled bytecode and no warning. The fix
/// asks what to write; these pin down each answer.
/// </summary>
public class ScriptSaveTests : IDisposable
{
    private readonly string _path = Fx.TempFile(".lua");

    // Written in the constructor rather than a field initialiser: DetectEncoding has to read the
    // file, so the file has to exist first.
    public ScriptSaveTests() =>
        File.WriteAllText(_path, "-- hand written source\nlocal a = 1\n", new System.Text.UTF8Encoding(false));

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
        foreach (var extra in new[] { Path.ChangeExtension(_path, ".luac") })
            try { if (File.Exists(extra)) File.Delete(extra); } catch { /* best effort */ }
    }

    private LuaEditorViewModel SourceScript() =>
        new(LuaScriptDocument.FromTextFile(_path), new DecompileSettings());

    /// <summary>Bytecode starts with ESC "Lua", which is what a .lua file must never be turned into.</summary>
    private static bool IsBytecode(string path)
    {
        byte[] head = new byte[4];
        using var fs = File.OpenRead(path);
        if (fs.Read(head, 0, 4) != 4) return false;
        return head[0] == 0x1B && head[1] == (byte)'L' && head[2] == (byte)'u' && head[3] == (byte)'a';
    }

    [Fact]
    public void SavingWithNoHook_KeepsTheFileAsSource()
    {
        var vm = SourceScript();
        vm.Text = "-- edited\nlocal a = 2\n";

        string status = vm.Save();

        Assert.False(IsBytecode(_path));
        Assert.Contains("-- edited", File.ReadAllText(_path));
        Assert.Contains("source", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChoosingSource_KeepsTheFileAsSource()
    {
        var vm = SourceScript();
        vm.Text = "-- edited\nlocal a = 3\n";
        vm.SaveFormatChooser = _ => SaveFormat.Source;

        vm.Save();

        Assert.False(IsBytecode(_path));
        Assert.Contains("-- edited", File.ReadAllText(_path));
    }

    [Fact]
    public void ChoosingCompiled_WritesBytecode()
    {
        var vm = SourceScript();
        vm.Text = "-- edited\nlocal a = 4\n";
        vm.SaveFormatChooser = _ => SaveFormat.Compiled;

        vm.Save();

        Assert.True(IsBytecode(_path));
    }

    [Fact]
    public void ChoosingBoth_WritesSourceAndABytecodeCopyBesideIt()
    {
        var vm = SourceScript();
        vm.Text = "-- edited\nlocal a = 5\n";
        vm.SaveFormatChooser = _ => SaveFormat.Both;

        vm.Save();

        string luac = Path.ChangeExtension(_path, ".luac");
        Assert.True(File.Exists(luac));
        Assert.False(IsBytecode(_path));
        Assert.True(IsBytecode(luac));
        Assert.Contains("-- edited", File.ReadAllText(_path));
    }

    [Fact]
    public void ChoosingBoth_LeavesTheDocumentPointedAtTheSource()
    {
        var vm = SourceScript();
        vm.SaveFormatChooser = _ => SaveFormat.Both;

        vm.Save();

        Assert.Equal(_path, vm.Document.FilePath);
        Assert.Equal(Path.GetFileName(_path), vm.Title);
    }

    [Fact]
    public void CancellingThePrompt_ChangesNothingOnDisk()
    {
        byte[] before = File.ReadAllBytes(_path);
        var vm = SourceScript();
        vm.Text = "-- this must not be written\n";
        vm.SaveFormatChooser = _ => null;

        string status = vm.Save();

        Assert.Equal("Save cancelled.", status);
        Assert.Equal(before, File.ReadAllBytes(_path));
        Assert.True(vm.IsDirty); // the edit is still unsaved
    }

    /// <summary>
    /// If the file is not on disk there is nothing to clobber, so it must not stop to ask.
    /// </summary>
    [Fact]
    public void AFileThatIsNotThereYet_IsWrittenAsSourceWithoutAsking()
    {
        var vm = new LuaEditorViewModel(LuaScriptDocument.FromTextFile(_path), new DecompileSettings());
        vm.Text = "-- the original is gone\n";
        vm.SaveFormatChooser = _ => throw new InvalidOperationException("must not be asked when there is nothing to lose");

        File.Delete(_path);

        vm.Save();

        Assert.False(IsBytecode(_path));
        Assert.Contains("-- the original is gone", File.ReadAllText(_path));
    }

    /// <summary>Save must clear the tab's dirty marker, which it used to leave set forever.</summary>
    [Fact]
    public void SavingASourceScript_ClearsTheDirtyMarker()
    {
        var vm = SourceScript();
        vm.Text = "-- edited\nlocal a = 6\n";
        Assert.True(vm.IsDirty);

        vm.Save();

        Assert.False(vm.IsDirty);
        Assert.DoesNotContain("*", vm.Header);
    }

    [Fact]
    public void SaveAs_ClearsTheDirtyMarker()
    {
        string target = Fx.TempFile(".luac");
        try
        {
            var vm = SourceScript();
            vm.Text = "-- edited\nlocal a = 7\n";

            vm.SaveAs(target);

            Assert.False(vm.IsDirty);
            Assert.True(IsBytecode(target));
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }

    /// <summary>A script with no path has nothing to clobber, so it should ask for Save As.</summary>
    [Fact]
    public void SavingAnUnsavedScript_AsksForSaveAsRatherThanWritingToTheWorkingDirectory()
    {
        var vm = new LuaEditorViewModel(LuaScriptDocument.CreateNewText(), new DecompileSettings());
        vm.Text = "-- new script\n";

        Assert.Throws<InvalidOperationException>(() => vm.Save());
    }
}