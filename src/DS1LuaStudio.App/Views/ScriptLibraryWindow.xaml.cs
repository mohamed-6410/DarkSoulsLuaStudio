using System.IO;
using System.Globalization;
using System.Windows;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.App.Views;

public partial class ScriptLibraryWindow : Window
{
    public sealed record LibraryItem(string Caption, ScriptDraft? Draft, EnemyScriptTemplate? Template);
    private readonly ScriptDraftStore _store;
    private readonly GameMode _mode;
    private readonly List<LibraryItem> _items = new();
    private bool _ready;
    private bool _validPreview = true;
    private ScriptDraft? _editingDraft;
    public string? ResultSource { get; private set; }
    public bool OpenNewScript { get; private set; }
    public GameMode ResultGameMode => _editingDraft?.GameMode ?? _mode;
    public static string DraftDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DS1LuaStudio", "Drafts");

    public ScriptLibraryWindow(GameMode mode, bool canInsert, string goalName, ScriptDraft? initialDraft = null, ScriptDraftStore? store = null)
    {
        InitializeComponent();
        _mode = mode;
        _store = store ?? new ScriptDraftStore(DraftDirectory);
        InsertButton.IsEnabled = canInsert;
        GoalNameBox.Text = goalName;
        LibraryPathText.Text = "Your drafts: " + _store.DirectoryPath;
        foreach (var draft in _store.Load()) _items.Add(new LibraryItem(draft.Caption, draft, null));
        foreach (var template in EnemyScriptTemplates.All) _items.Add(new LibraryItem("Template · " + template.Name, null, template));
        if (initialDraft is not null) _items.Insert(0, new LibraryItem(initialDraft.Caption, initialDraft, null));
        _ready = true;
        RefreshList();
        LibraryList.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        var selected = LibraryList.SelectedItem;
        string filter = FilterBox.Text.Trim();
        LibraryList.ItemsSource = _items.Where(i => (GameOnlyCheck.IsChecked != true || i.Draft is null || i.Draft.GameMode == _mode) &&
            (filter.Length == 0 || (i.Caption + " " + i.Draft?.Notes + " " + i.Draft?.Source + " " + i.Template?.Description).Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
        LibraryList.SelectedItem = selected;
    }
    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (_ready) RefreshList(); }
    private void OnGameFilterChanged(object sender, RoutedEventArgs e) => RefreshList();
    private void OnSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || LibraryList.SelectedItem is not LibraryItem item) return;
        _editingDraft = item.Draft;
        NameBox.Text = item.Draft?.Name ?? item.Template!.Name;
        NotesBox.Text = item.Draft?.Notes ?? "Choose character-specific attack IDs; register the callbacks in your goal/global tables.";
        DescriptionText.Text = item.Draft is { } draft ? $"Saved {draft.GameMode} draft · {draft.Origin}" : item.Template!.Description;
        TemplateFields.Visibility = item.Template is null ? Visibility.Collapsed : Visibility.Visible;
        if (item.Template is { } template)
        {
            ActBox.IsEnabled = template.Id is not ("enemy" or "boss");
            FollowupBox.IsEnabled = template.Id is "combo" or "boss";
            PhaseBox.IsEnabled = template.Id == "boss";
            if (!ActBox.IsEnabled) ActBox.Text = "4";
            if (!FollowupBox.IsEnabled) FollowupBox.Text = "3001";
            if (!PhaseBox.IsEnabled) PhaseBox.Text = "0.5";
        }
        RenameFields.Visibility = item.Draft is null ? Visibility.Collapsed : Visibility.Visible;
        if (item.Draft is { } saved)
        {
            SourceBox.Text = saved.Source;
            RenameBox.Text = LuaFunctionDrafts.FindFunctions(saved.Source).FirstOrDefault()?.Name ?? "";
            _validPreview = true;
            StatusText.Text = "The complete function is copied. Keep any helpers, constants or outer locals it depends on when reusing it.";
        }
        else GeneratePreview();
    }
    private void OnParametersChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_ready && LibraryList.SelectedItem is LibraryItem { Template: not null })
        { _validPreview = false; StatusText.Text = "Click Generate preview to apply your parameter changes."; }
    }
    private void OnGeneratePreview(object sender, RoutedEventArgs e) => GeneratePreview();
    private void GeneratePreview()
    {
        if (LibraryList.SelectedItem is not LibraryItem { Template: { } template }) return;
        try
        {
            SourceBox.Text = template.Render(GoalNameBox.Text.Trim(), int.Parse(ActBox.Text, CultureInfo.InvariantCulture), int.Parse(AttackBox.Text, CultureInfo.InvariantCulture),
                int.Parse(FollowupBox.Text, CultureInfo.InvariantCulture), double.Parse(RangeBox.Text, CultureInfo.InvariantCulture), double.Parse(PhaseBox.Text, CultureInfo.InvariantCulture));
            _validPreview = true;
            StatusText.Text = "Attack IDs are examples. Match them to your character, then register the callback names in .luainfo / .luagnl. Templates are starting points to tune in game.";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) { _validPreview = false; StatusText.Text = ex.Message; }
    }
    private bool CheckSource()
    {
        if (!_validPreview) { StatusText.Text = "Generate the preview after changing parameters."; return false; }
        if (string.IsNullOrWhiteSpace(SourceBox.Text)) { StatusText.Text = "The preview is empty."; return false; }
        return true;
    }
    private void OnSaveDraft(object sender, RoutedEventArgs e)
    {
        if (!CheckSource()) return;
        try
        {
            var draft = new ScriptDraft { Id = _editingDraft?.Id ?? Guid.NewGuid(), Name = NameBox.Text.Trim(), Source = SourceBox.Text,
                GameMode = ResultGameMode, Notes = NotesBox.Text, Origin = _editingDraft?.Origin ?? "Enemy template", SavedUtc = DateTime.UtcNow };
            _store.Save(draft);
            _items.RemoveAll(i => i.Draft?.Id == draft.Id);
            var item = new LibraryItem(draft.Caption, draft, null); _items.Insert(0, item);
            FilterBox.Text = ""; RefreshList(); LibraryList.SelectedItem = item;
            StatusText.Text = "Draft saved. It will be available after restarting the editor.";
        }
        catch (Exception ex) { StatusText.Text = "Could not save draft: " + ex.Message; }
    }
    private void OnRename(object sender, RoutedEventArgs e)
    {
        try
        {
            var function = LuaFunctionDrafts.FindFunctions(SourceBox.Text).FirstOrDefault() ?? throw new ArgumentException("No complete named function in the preview.");
            SourceBox.Text = LuaFunctionDrafts.Rename(SourceBox.Text, function.Name, RenameBox.Text.Trim());
            StatusText.Text = "Function and self-references renamed in the preview. Strings and comments were preserved.";
        }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; }
    }
    private void OnCopy(object sender, RoutedEventArgs e) { if (CheckSource()) System.Windows.Clipboard.SetText(SourceBox.Text); }
    private void OnInsert(object sender, RoutedEventArgs e) { if (CheckSource()) { ResultSource = SourceBox.Text; DialogResult = true; } }
    private void OnNewScript(object sender, RoutedEventArgs e) { if (CheckSource()) { OpenNewScript = true; ResultSource = SourceBox.Text; DialogResult = true; } }
}
