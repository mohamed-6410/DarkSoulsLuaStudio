using DS1LuaStudio.App;
using DS1LuaStudio.App.Views;
using DS1LuaStudio.App.ViewModels;
using DS1LuaStudio.App.Services;
using DS1LuaStudio.Core;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

class Program
{
    [STAThread]
    static void Main()
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        window.Opacity = 0;
        window.ShowInTaskbar = false;
        window.Show();
        window.Hide();
        var model = (MainViewModel)window.DataContext;
        model.ReplaceSettings(new AppSettings { GameMode = GameMode.DS3 });
        model.Open(Path.GetFullPath("DS3-Luascripts/script/m20_01_00_00.luabnd.dcx"));
        var archive = (ArchiveViewModel)model.SelectedDocument;
        archive.SelectedEntry = archive.Entries.First(e => e.LooksLikeLua);
        model.OpenEntryCommand.Execute(null);
        var script = (LuaEditorViewModel)model.SelectedDocument;
        if (!script.GoalLink.Exists || script.TargetSizeT != 8) throw new Exception("DS3 script link/layout failed");
        Render(window, "tools/UiProbe/ds3-editor.png", 1340, 760);
        var activeEditor = Descendants((DependencyObject)window.Content).OfType<ICSharpCode.AvalonEdit.TextEditor>().Single();
        var completion = (ICSharpCode.AvalonEdit.CodeCompletion.CompletionWindow)typeof(MainWindow).GetMethod("CreateCompletionWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window,
            new object[] { activeEditor, 0, "Get", model.AiNames.KnownSymbols.Where(s => s.StartsWith("Get")).OrderBy(s => s).ToList() })!;
        if (completion.FlowDirection != FlowDirection.LeftToRight || completion.CompletionList.ListBox.FlowDirection != FlowDirection.LeftToRight) throw new Exception("Completion direction is reversed");
        RenderWindow(completion, "tools/UiProbe/autocomplete.png", 480, 300);
        completion.Close();
        var act = LuaFunctionDrafts.FindFunctions(script.Text).First(f => f.Name.EndsWith("_Act04"));
        var draft = new ScriptDraft { Name = act.Name, Source = act.Extract(script.Text), GameMode = GameMode.DS3, Origin = script.Document.Origin };
        if (!LuaBytecodeService.Decompile(LuaBytecodeService.Compile(draft.Source, LuaDialect.Lua50, targetSizeT: 8)).Success) throw new Exception("Captured Act04 cannot compile");
        Invoke(window, "OnCollapseFunctions");
        Render(window, "tools/UiProbe/ds3-folded.png", 1340, 760);
        if (script.CollapsedFunctions.Count == 0) throw new Exception("No functions collapsed");
        model.RevealScriptGoalCommand.Execute(null);
        if (model.SelectedDocument is not LuaInfoViewModel info || info.SelectedRow is null) throw new Exception("Goal navigation failed");
        Render(window, "tools/UiProbe/ds3-goals.png", 940, 560);
        model.SelectedDocument = script;
        Render(window, "tools/UiProbe/ds3-folded-return.png", 1340, 760);
        if (script.CollapsedFunctions.Count == 0) throw new Exception("Collapsed state lost during tab switch");
        model.SelectedDocument = archive;
        archive.SelectedEntry = archive.Entries.First(e => e.LooksLikeLuaGnl);
        model.OpenEntryCommand.Execute(null);
        if (model.SelectedDocument is not LuaGnlViewModel) throw new Exception("GNL open failed");
        Render(window, "tools/UiProbe/ds3-globals.png", 1340, 760);
        var store = new ScriptDraftStore(Path.Combine(Path.GetTempPath(), "LuaStudioUI-" + Guid.NewGuid()));
        try
        {
            var library = new ScriptLibraryWindow(GameMode.DS3, true, "MyEnemy100000", draft, store);
            RenderWindow(library, "tools/UiProbe/function-drafts.png", 1080, 760);
            Invoke(library, "OnSaveDraft");
            if (store.Load().Single().Source != draft.Source) throw new Exception("Draft storage lost source");
            var list = (System.Windows.Controls.ListBox)library.FindName("LibraryList");
            list.SelectedItem = list.Items.Cast<ScriptLibraryWindow.LibraryItem>().First(i => i.Template?.Id == "boss");
            RenderWindow(library, "tools/UiProbe/boss-template.png", 1080, 760);
        }
        finally { if (Directory.Exists(store.DirectoryPath)) Directory.Delete(store.DirectoryPath, true); }
        Console.WriteLine("DS3 script, goal navigation and GNL UI rendered successfully.");
    }
    static void Render(MainWindow window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        foreach (var editor in Descendants(content).OfType<ICSharpCode.AvalonEdit.TextEditor>())
            typeof(MainWindow).GetMethod("OnEditorLoaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, new object[] { editor, new RoutedEventArgs() });
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    static void RenderWindow(Window window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    static void Invoke(Window window, string method) => window.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
