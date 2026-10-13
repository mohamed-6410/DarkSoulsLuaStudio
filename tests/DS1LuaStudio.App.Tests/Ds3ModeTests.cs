using DS1LuaStudio.App.Services;
using DS1LuaStudio.App.ViewModels;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.App.Tests;

public class Ds3ModeTests
{
    [Fact]
    public void ModeChangesNewDocumentDefaultsAndKeepsLoadedChunkWidth()
    {
        var vm = new MainViewModel();
        vm.ReplaceSettings(new AppSettings { GameMode = GameMode.DS3 });
        vm.NewLuaScriptCommand.Execute(null);
        Assert.Equal(8, ((LuaEditorViewModel)vm.SelectedDocument!).TargetSizeT);
        vm.NewLuaGnlCommand.Execute(null);
        Assert.True(((LuaGnlViewModel)vm.SelectedDocument!).Document.LongFormat);
        vm.NewLuaInfoCommand.Execute(null);
        Assert.True(((LuaInfoViewModel)vm.SelectedDocument!).Document.LongFormat);
        Assert.Contains("GetDist", vm.Symbols);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lua");
        try
        {
            File.WriteAllBytes(path, LuaBytecodeService.Compile("return 1", LuaDialect.Lua50, targetSizeT: 4));
            Assert.True(vm.Open(path));
            var loaded = (LuaEditorViewModel)vm.SelectedDocument!;
            Assert.Equal(4, loaded.TargetSizeT);
            loaded.Text += "\n-- unsaved work";
            vm.ReplaceSettings(new AppSettings { GameMode = GameMode.DS1 });
            Assert.Equal(4, loaded.TargetSizeT);
            Assert.Contains("unsaved work", loaded.Text);
            Assert.True(loaded.IsDirty);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void F12FollowsOpenScriptDefinition()
    {
        var vm = new MainViewModel();
        var callee = new LuaEditorViewModel(LuaScriptDocument.CreateNewText("helper.lua"), new DecompileSettings())
            { Text = "function MyHelper(ai) return 1 end" };
        var caller = new LuaEditorViewModel(LuaScriptDocument.CreateNewText("caller.lua"), new DecompileSettings())
            { Text = "MyHelper(ai)" };
        vm.Documents.Add(callee);
        vm.Documents.Add(caller);
        vm.SelectedDocument = caller;
        Assert.True(vm.NavigateToSymbol("MyHelper"));
        Assert.Same(callee, vm.SelectedDocument);
        Assert.Equal(9, callee.CaretOffset);
    }
}
