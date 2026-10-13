using DS1LuaStudio.App.ViewModels;
using DS1LuaStudio.Core;
using Xunit;

namespace DS1LuaStudio.App.Tests;

/// <summary>Shared scaffolding: a goal table document and a view model wired to it.</summary>
internal static class Fx
{
    public static string TempFile(string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ds1studio-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + extension);
    }

    /// <summary>A goal table with <paramref name="names"/> as its goal names.</summary>
    public static (LuaInfoDocument Doc, LuaInfoViewModel Vm) GoalTable(params string[] names)
    {
        var doc = LuaInfoDocument.CreateNew();
        doc.Goals.Clear();
        foreach (string name in names) doc.AddGoal();
        for (int i = 0; i < names.Length; i++) doc.Goals[i].Name = names[i];
        doc.Reindex();
        return (doc, new LuaInfoViewModel(doc));
    }

    public static LuaInfoViewModel AsActive(MainViewModel vm, LuaInfoViewModel info)
    {
        vm.Documents.Add(info);
        vm.SelectedDocument = info;
        return info;
    }
}

public class GoalRowCommandTests
{
    /// <summary>
    /// Delete used to act on every row the filter was showing, so one click wiped the table. It
    /// must only ever touch the rows the grid had selected.
    /// </summary>
    [Fact]
    public void Delete_RemovesOnlyTheSelectedRows()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie", "delta");
        var victim = doc.Goals[1];

        info.SetSelectedRows(new[] { victim });
        info.RemoveRows(info.SelectedRows);

        Assert.Equal(3, doc.Goals.Count);
        Assert.DoesNotContain(doc.Goals, r => r.Name == "bravo");
        Assert.Contains(doc.Goals, r => r.Name == "alpha");
        Assert.Contains(doc.Goals, r => r.Name == "charlie");
        Assert.Contains(doc.Goals, r => r.Name == "delta");
    }

    [Fact]
    public void Delete_RemovesEverySelectedRow_WhenSeveralArePicked()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie", "delta");

        info.SetSelectedRows(new[] { doc.Goals[0], doc.Goals[3] });
        info.RemoveRows(info.SelectedRows);

        Assert.Equal(new[] { "bravo", "charlie" }, doc.Goals.Select(g => g.Name).ToArray());
    }

    [Fact]
    public void Delete_DoesNotTouchRowsTheFilterIsHiding()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie");

        info.Filter = "bravo";
        info.SetSelectedRows(new[] { doc.Goals[1] });
        info.RemoveRows(info.SelectedRows);

        Assert.Equal(new[] { "alpha", "charlie" }, doc.Goals.Select(g => g.Name).ToArray());
    }

    /// <summary>Delete and Duplicate must be inert with nothing selected.</summary>
    [Fact]
    public void DeleteAndDuplicate_AreDisabled_WhenNothingIsSelected()
    {
        var (_, info) = Fx.GoalTable("alpha", "bravo");
        var vm = new MainViewModel();
        Fx.AsActive(vm, info);

        Assert.False(vm.DeleteGoalCommand.CanExecute(null));
        Assert.False(vm.DuplicateGoalCommand.CanExecute(null));
        Assert.False(vm.MoveGoalUpCommand.CanExecute(null));
        Assert.False(vm.MoveGoalDownCommand.CanExecute(null));
    }

    [Fact]
    public void RowCommands_EnableOnceARowIsSelected()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie");
        var vm = new MainViewModel();
        Fx.AsActive(vm, info);

        info.SelectedRow = doc.Goals[1];

        Assert.True(vm.DeleteGoalCommand.CanExecute(null));
        Assert.True(vm.DuplicateGoalCommand.CanExecute(null));
        Assert.True(vm.MoveGoalUpCommand.CanExecute(null));
        Assert.True(vm.MoveGoalDownCommand.CanExecute(null));
    }

    /// <summary>Move buttons should not be live at the ends of the list.</summary>
    [Fact]
    public void Move_IsDisabledAtTheEndsOfTheList()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo");
        var vm = new MainViewModel();
        Fx.AsActive(vm, info);

        info.SelectedRow = doc.Goals[0];
        Assert.False(vm.MoveGoalUpCommand.CanExecute(null));
        Assert.True(vm.MoveGoalDownCommand.CanExecute(null));

        info.SelectedRow = doc.Goals[1];
        Assert.True(vm.MoveGoalUpCommand.CanExecute(null));
        Assert.False(vm.MoveGoalDownCommand.CanExecute(null));
    }

    /// <summary>The selected row must stay selected after a move, or the buttons all grey out.</summary>
    [Fact]
    public void Move_ReloadsItsCommandsSoTheSelectionStaysLive()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie");
        var vm = new MainViewModel();
        Fx.AsActive(vm, info);

        info.SelectedRow = doc.Goals[0];
        vm.MoveGoalDownCommand.Execute(null);

        Assert.Equal(new[] { "bravo", "alpha", "charlie" }, doc.Goals.Select(g => g.Name).ToArray());
        Assert.True(vm.MoveGoalUpCommand.CanExecute(null));
        Assert.True(vm.MoveGoalDownCommand.CanExecute(null));

        // Once it reaches the bottom the down button has to grey out again.
        vm.MoveGoalDownCommand.Execute(null);
        Assert.Equal(new[] { "bravo", "charlie", "alpha" }, doc.Goals.Select(g => g.Name).ToArray());
        Assert.False(vm.MoveGoalDownCommand.CanExecute(null));
    }

    [Fact]
    public void Delete_ViaTheCommand_RemovesOnlyTheSelectedRow()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie");
        var vm = new MainViewModel();
        Fx.AsActive(vm, info);

        info.SetSelectedRows(new[] { doc.Goals[2] });
        Assert.True(vm.DeleteGoalCommand.CanExecute(null));

        vm.DeleteGoalCommand.Execute(null);

        Assert.Equal(new[] { "alpha", "bravo" }, doc.Goals.Select(g => g.Name).ToArray());
    }

    [Fact]
    public void RemovingRows_ClearsThemFromTheSelection()
    {
        var (doc, info) = Fx.GoalTable("alpha", "bravo", "charlie");
        var vm = new MainViewModel();
        Fx.AsActive(vm, info);

        info.SelectedRow = doc.Goals[1];
        info.SetSelectedRows(new[] { doc.Goals[1], doc.Goals[2] });
        vm.DeleteGoalCommand.Execute(null);

        Assert.False(info.HasGoalSelection);
        Assert.Null(info.SelectedRow);
        Assert.False(vm.DeleteGoalCommand.CanExecute(null));
    }
}

public class GoalTableDirtyTests
{
    /// <summary>
    /// A cell edit changes the row, not the collection, so nothing used to mark the tab dirty and
    /// the edits were lost silently on close.
    /// </summary>
    [Fact]
    public void EditingACell_MarksTheTabDirty()
    {
        var (_, info) = Fx.GoalTable("alpha", "bravo");
        info.IsDirty = false;

        info.Goals[0].Name = "renamed";

        Assert.True(info.IsDirty);
        Assert.Contains("*", info.Header);
    }

    [Fact]
    public void TogglingAnInterruptFlag_MarksTheTabDirty()
    {
        var (_, info) = Fx.GoalTable("alpha", "bravo");
        info.IsDirty = false;

        info.Goals[1].LogicInterrupt = true;

        Assert.True(info.IsDirty);
    }

    [Fact]
    public void EditingALogicInterruptName_RefreshesTheLogicCount()
    {
        var (_, info) = Fx.GoalTable("alpha", "bravo");
        info.IsDirty = false;

        info.Goals[1].LogicInterruptName = "Some_Interupt";

        Assert.Equal(1, info.LogicCount);
    }

    [Fact]
    public void AddingARow_MarksTheTabDirty()
    {
        var (_, info) = Fx.GoalTable("alpha");
        info.IsDirty = false;

        info.AddRow();

        Assert.True(info.IsDirty);
    }

    /// <summary>Saving must clear the marker, otherwise the tab looks edited forever.</summary>
    [Fact]
    public void Save_ClearsTheDirtyMarker()
    {
        string path = Fx.TempFile(".luainfo");
        var doc = LuaInfoDocument.CreateNew();
        doc.Goals.Clear();
        doc.AddGoal();
        doc.Goals[0].Name = "saved_goal";
        doc.Reindex();
        doc.Save(path);

        var info = new LuaInfoViewModel(doc);
        info.Goals[0].Name = "edited";
        Assert.True(info.IsDirty);

        info.Save();

        Assert.False(info.IsDirty);
        Assert.DoesNotContain("*", info.Header);
    }
}