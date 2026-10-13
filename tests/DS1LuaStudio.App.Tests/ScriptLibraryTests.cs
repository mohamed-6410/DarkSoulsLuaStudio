using DS1LuaStudio.App.ViewModels;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.App.Tests;

public class ScriptLibraryTests
{
    [Fact]
    public void InsertionRejectsDuplicateFunctionNames()
    {
        var model = new MainViewModel();
        var editor = new LuaEditorViewModel(LuaScriptDocument.CreateNewText(), new DecompileSettings()) { Text = "function Enemy_Act04(ai) return ai end" };
        model.Documents.Add(editor); model.SelectedDocument = editor;
        Assert.False(model.CanInsertDraft("function Enemy_Act04(ai) return 1 end"));
        Assert.Contains("already exists", model.Status);
        Assert.True(model.CanInsertDraft("function Enemy_Act05(ai) return 1 end"));
        Assert.Equal("function Enemy_Act04(ai) return ai end", editor.Text);
    }

    [Fact]
    public void NewScriptFromDraftIsUnsavedAndUsesItsGameWidth()
    {
        var model = new MainViewModel();
        model.OpenDraftScript("function Enemy_Act04(ai) return ai end", GameMode.DS3);
        var editor = Assert.IsType<LuaEditorViewModel>(model.SelectedDocument);
        Assert.True(editor.IsDirty);
        Assert.Null(editor.Document.FilePath);
        Assert.Equal(8, editor.TargetSizeT);
    }
}
