using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DS1LuaStudio.App.Mvvm;
using DS1LuaStudio.App.Services;
using DS1LuaStudio.App.ViewModels;
using DS1LuaStudio.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Folding;
using System.Windows.Threading;
using System.Text.RegularExpressions;

namespace DS1LuaStudio.App.Views;

using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using WpfTabControl = System.Windows.Controls.TabControl;
using WpfTextBlock = System.Windows.Controls.TextBlock;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private AppSettings _settings;

    /// <summary>
    /// One wired editor and the document it is currently showing.
    /// <para>
    /// The TabControl hosts a single ContentPresenter, and WPF reuses the existing visual tree
    /// whenever the new tab uses the same DataTemplate - it only swaps the DataContext. So going
    /// from one .lua tab to another raises <em>neither</em> Loaded nor Unloaded: the editor has to
    /// follow DataContext instead, or it stays showing whichever script was opened first.
    /// </para>
    /// </summary>
    private sealed class EditorLink
    {
        private readonly MainWindow _owner;
        private readonly TextEditor _editor;

        public LuaEditorViewModel ViewModel { get; }

        private TextDocument? _document;

        public EditorLink(MainWindow owner, TextEditor editor, LuaEditorViewModel viewModel)
        {
            _owner = owner;
            _editor = editor;
            ViewModel = viewModel;
        }

        /// <summary>
        /// Subscribes to the editor's document. Called after the text has been seeded, because
        /// assigning <c>TextEditor.Text</c> can replace the underlying document outright.
        /// </summary>
        public void Attach()
        {
            _document = _editor.TextArea.Document;
            _document.TextChanged += OnDocumentChanged;
            ViewModel.PropertyChanged += OnViewModelChanged;
        }

        public void Detach()
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            if (_document is not null) _document.TextChanged -= OnDocumentChanged;
            _document = null;
        }

        private void OnDocumentChanged(object? sender, EventArgs e)
        {
            if (_owner._syncing) return;
            ViewModel.Text = _editor.Text;
            _owner.QueueFoldings(_editor);
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LuaEditorViewModel.Text)) return;
            if (string.Equals(_editor.Text, ViewModel.Text, StringComparison.Ordinal)) return;

            _owner._syncing = true;
            try
            {
                _editor.Text = ViewModel.Text;
                _owner.UpdateFoldings(_editor);
            }
            finally
            {
                _owner._syncing = false;
            }
        }
    }

    private readonly Dictionary<TextEditor, EditorLink> _wiredEditors = new();

    /// <summary>Editors already watching their own DataContext, so the handler is only added once.</summary>
    private readonly HashSet<TextEditor> _watchedEditors = new();

    /// <summary>Guards against the editor and the view model echoing text back at each other.</summary>
    private bool _syncing;
    private CompletionWindow? _completion;
    private readonly Dictionary<TextEditor, FoldingManager> _foldings = new();
    private readonly Dictionary<TextEditor, DispatcherTimer> _foldingTimers = new();

    private void QueueFoldings(TextEditor editor)
    {
        if (!_foldingTimers.TryGetValue(editor, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) => { timer.Stop(); UpdateFoldings(editor); };
            _foldingTimers[editor] = timer;
        }
        timer.Stop(); timer.Start();
    }
    private void CaptureFoldings(TextEditor editor, LuaEditorViewModel model)
    {
        if (!_foldings.TryGetValue(editor, out var manager)) return;
        model.CollapsedFunctions.Clear();
        foreach (var section in manager.AllFoldings.Where(f => f.IsFolded))
            if (section.Tag is string name) model.CollapsedFunctions.Add(name);
    }
    private void UpdateFoldings(TextEditor editor, bool capture = true)
    {
        if (editor.DataContext is not LuaEditorViewModel model) return;
        if (!_foldings.TryGetValue(editor, out var manager))
        {
            manager = FoldingManager.Install(editor.TextArea); _foldings[editor] = manager;
            FoldingMargin.SetFoldingMarkerBrush(editor.TextArea, Brush("BrushTextSecondary"));
            FoldingMargin.SetFoldingMarkerBackgroundBrush(editor.TextArea, Brush("EditorBackground"));
            FoldingMargin.SetSelectedFoldingMarkerBrush(editor.TextArea, Brush("BrushAccent"));
        }
        if (capture) CaptureFoldings(editor, model);
        var functions = LuaFunctionDrafts.FindFunctions(editor.Text);
        var regions = functions.Where(f => editor.Document.GetLineByOffset(f.Start).EndOffset < f.Start + f.Length).Select(f =>
        {
            int start = editor.Document.GetLineByOffset(f.Start).EndOffset;
            return new NewFolding(start, f.Start + f.Length) { Name = f.Name, DefaultClosed = model.CollapsedFunctions.Contains(f.Name), IsDefinition = true };
        }).Where(f => f.EndOffset > f.StartOffset).OrderBy(f => f.StartOffset).ToList();
        manager.UpdateFoldings(regions, -1);
        foreach (var section in manager.AllFoldings)
        {
            string name = regions.First(r => r.StartOffset == section.StartOffset).Name;
            section.Tag = name;
            section.Title = "…";
            section.IsFolded = model.CollapsedFunctions.Contains(name);
        }
    }
    private void OnCollapseFunctions(object sender, RoutedEventArgs e) => SetFunctionFolding(true);
    private void OnExpandFunctions(object sender, RoutedEventArgs e) => SetFunctionFolding(false);
    private void SetFunctionFolding(bool folded)
    {
        if (FindActiveEditor() is not { } editor) return;
        UpdateFoldings(editor);
        foreach (var section in _foldings[editor].AllFoldings) section.IsFolded = folded;
        if (editor.DataContext is LuaEditorViewModel model) CaptureFoldings(editor, model);
    }

    private LuaFunctionSpan? FunctionAtCursor(TextEditor editor)
    {
        var function = LuaFunctionDrafts.AtCaret(editor.Text, editor.CaretOffset);
        if (function is null) DialogService.Info("Place the cursor inside a complete function, such as Act04, then try again.");
        return function;
    }
    private void OnCopyFunction(object sender, RoutedEventArgs e)
    {
        if (FindActiveEditor() is { } editor && FunctionAtCursor(editor) is { } function)
            System.Windows.Clipboard.SetText(function.Extract(editor.Text));
    }
    private void OnSaveFunctionDraft(object sender, RoutedEventArgs e)
    {
        if (FindActiveEditor() is not { } editor || FunctionAtCursor(editor) is not { } function || editor.DataContext is not LuaEditorViewModel model) return;
        var draft = new ScriptDraft { Name = function.Name, Source = function.Extract(editor.Text),
            GameMode = model.Document.SizeTSize == 8 ? GameMode.DS3 : model.Document.SizeTSize == 4 ? GameMode.DS1 : _model.GameMode,
            Origin = model.Document.Origin,
            Notes = LuaFunctionDrafts.ReferencedOuterLocals(editor.Text, function) is { Count: > 0 } dependencies
                ? "Review outer locals/constants: " + string.Join(", ", dependencies) + ". Keep required helper functions too."
                : "Keep any referenced helpers, constants and outer locals with this function when reusing it." };
        ShowScriptLibrary(draft);
    }
    private void OnScriptLibrary(object sender, RoutedEventArgs e) => ShowScriptLibrary();
    private void ShowScriptLibrary(ScriptDraft? draft = null)
    {
        _completion?.Close();
        string goalName = (_model.SelectedDocument as LuaEditorViewModel)?.GoalLink?.Name ?? "MyEnemy100000";
        foreach (string suffix in new[] { "_Battle", "Battle", "_Logic", "Logic" })
            if (goalName.EndsWith(suffix, StringComparison.Ordinal)) { goalName = goalName[..^suffix.Length]; break; }
        var dialog = new ScriptLibraryWindow(draft?.GameMode ?? _model.GameMode, _model.ActiveIsEditor, goalName, draft) { Owner = this };
        while (dialog.ShowDialog() == true && dialog.ResultSource is { } source)
        {
            if (dialog.OpenNewScript) { _model.OpenDraftScript(source, dialog.ResultGameMode); return; }
            if (!_model.CanInsertDraft(source)) { DialogService.Info(_model.Status); dialog = new ScriptLibraryWindow(dialog.ResultGameMode, true, goalName,
                new ScriptDraft { Name = "Rename before insertion", Source = source, GameMode = dialog.ResultGameMode }) { Owner = this }; continue; }
            if (FindActiveEditor() is not { } editor) return;
            var containing = LuaFunctionDrafts.AtCaret(editor.Text, editor.CaretOffset);
            int offset = containing is null ? editor.CaretOffset : containing.Start + containing.Length;
            using (editor.Document.RunUpdate()) editor.Document.Insert(offset, "\n\n" + source.Trim() + "\n");
            editor.CaretOffset = offset + 2;
            editor.Focus();
            return;
        }
    }

    private void ShowCompletion(TextEditor editor, bool explicitRequest = false)
    {
        if (!_model.EnableAutocomplete || _completion is not null) return;
        int end = editor.CaretOffset;
        int start = end;
        while (start > 0 && (char.IsLetterOrDigit(editor.Document.GetCharAt(start - 1)) || editor.Document.GetCharAt(start - 1) == '_')) start--;
        string prefix = editor.Document.GetText(start, end - start);
        if (!explicitRequest && prefix.Length == 0 && (end == 0 || editor.Document.GetCharAt(end - 1) is not ('.' or ':'))) return;
        // Avoid opening suggestions in comments and string literals.
        string line = editor.Document.GetText(editor.Document.GetLineByOffset(end));
        int column = end - editor.Document.GetLineByOffset(end).Offset;
        string beforeCaret = line[..Math.Min(column, line.Length)];
        if (beforeCaret.Contains("--") || beforeCaret.Count(c => c == '"') % 2 != 0 || beforeCaret.Count(c => c == '\'') % 2 != 0) return;
        var suggestions = _model.AiNames.KnownSymbols
            .Concat(LuaFunctionDrafts.Tokens(editor.Text).Select(t => t.Text).Where(LuaFunctionDrafts.IsIdentifier))
            .Concat(new[] { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" })
            .Distinct(StringComparer.Ordinal).Where(s => s.Contains(prefix, StringComparison.OrdinalIgnoreCase) && s != prefix)
            .OrderByDescending(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ThenBy(s => s, StringComparer.Ordinal).ToList();
        if (suggestions.Count == 0) return;
        var window = CreateCompletionWindow(editor, start, prefix, suggestions);
        _completion = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_completion, window)) _completion = null; };
        window.Show();
    }

    private CompletionWindow CreateCompletionWindow(TextEditor editor, int start, string prefix, IReadOnlyList<string> suggestions)
    {
        var window = new CompletionWindow(editor.TextArea) { StartOffset = start, CloseAutomatically = true,
            FlowDirection = System.Windows.FlowDirection.LeftToRight, Width = 480, MaxHeight = 300, MinWidth = 300, FontSize = 13 };
        window.Background = Brush("BrushBgPanel");
        window.Foreground = Brush("BrushTextPrimary");
        var scriptFunctions = LuaFunctionDrafts.FindFunctions(editor.Text).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var suggestion in suggestions)
        {
            string kind = scriptFunctions.Contains(suggestion) ? "Function" : _model.AiNames.Goals.ContainsKey(suggestion) || suggestion.StartsWith("GOAL_") || suggestion.StartsWith("TARGET_") ? "Constant" : _model.AiNames.KnownSymbols.Contains(suggestion) ? "AI" : "Script";
            window.CompletionList.CompletionData.Add(new LuaCompletionData(suggestion, _model.AiNames.Describe(suggestion), kind, suggestion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 10 : 0));
        }
        window.CompletionList.IsFiltering = true;
        window.CompletionList.ApplyTemplate();
        window.CompletionList.ListBox.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        window.CompletionList.ListBox.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        window.CompletionList.ListBox.Background = Brush("BrushBgPanel");
        window.CompletionList.ListBox.Foreground = Brush("BrushTextPrimary");
        window.CompletionList.ListBox.ItemContainerStyle = Application.Current?.TryFindResource(typeof(System.Windows.Controls.ListBoxItem)) as Style;
        window.CompletionList.SelectItem(prefix);
        return window;
    }

    private void OnCompletionTextEntered(object sender, TextCompositionEventArgs e)
    {
        var editor = _wiredEditors.Keys.FirstOrDefault(t => ReferenceEquals(t.TextArea, sender));
        if (editor is not null && e.Text.Length > 0 && (char.IsLetterOrDigit(e.Text[0]) || e.Text[0] is '_' or '.' or ':')) ShowCompletion(editor);
    }
    private void OnCompletionTextEntering(object sender, TextCompositionEventArgs e)
    {
        if (_completion is not null && e.Text.Length > 0 && e.Text[0] is '(' or '.')
            _completion.CompletionList.RequestInsertion(e);
        else if (_completion is not null && e.Text.Length > 0 && !(char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_'))
            _completion.Close();
    }
    private void OnCompletionKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control && sender is TextEditor editor)
        { ShowCompletion(editor, true); e.Handled = true; }
        if (e.Key == Key.F12 && sender is TextEditor active)
        {
            int start = active.CaretOffset, end = start;
            while (start > 0 && (char.IsLetterOrDigit(active.Document.GetCharAt(start - 1)) || active.Document.GetCharAt(start - 1) == '_')) start--;
            while (end < active.Document.TextLength && (char.IsLetterOrDigit(active.Document.GetCharAt(end)) || active.Document.GetCharAt(end) == '_')) end++;
            if (_model.NavigateToSymbol(active.Document.GetText(start, end - start)))
                Dispatcher.BeginInvoke(() =>
                {
                    foreach (var target in _wiredEditors.Keys)
                        if (target.DataContext is LuaEditorViewModel vm && ReferenceEquals(vm, _model.SelectedDocument)) RestoreEditorView(target, vm);
                });
            e.Handled = true;
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        ThemeManager.Apply(_settings);

        _model = new MainViewModel();
        _model.Settings = _settings;
        _model.ReplaceSettings(_settings);
        DataContext = _model;
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AiNames))
                foreach (var editor in _wiredEditors.Keys) editor.SyntaxHighlighting = Ds1LuaHighlighting.Instance(_model.AiNames);
            if (e.PropertyName == nameof(MainViewModel.EnableAutocomplete) && !_model.EnableAutocomplete) _completion?.Close();
        };

        _model.TextInsertRequested = InsertAtCursor;
        _model.FindRequested = FocusFindBox;
        _model.SettingsApplied = ApplySettings;

        // Bound to the commands themselves, not to wrappers that call Execute. These used to be
        // wrappers, which bypassed CanExecute entirely: File -> Save greyed out with nothing open
        // while Ctrl+S still fired. An input binding re-checks CanExecute, so the two now agree.
        InputBindings.Add(new KeyBinding(_model.SaveCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_model.SaveAsCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(_model.OpenFileCommand, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_model.RequestFindCommand, Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnCopyFunction(this, new RoutedEventArgs()), () => _model.ActiveIsEditor), Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnSaveFunctionDraft(this, new RoutedEventArgs()), () => _model.ActiveIsEditor), Key.D, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnScriptLibrary(this, new RoutedEventArgs())), Key.L, ModifierKeys.Control | ModifierKeys.Shift));
    }

    /// <summary>
    /// Brings the DS1-aware Lua highlighting into each editor as it is realised.
    /// <para>
    /// AvalonEdit 6 exposes <c>TextEditor.Text</c> as a plain CLR property, not a dependency
    /// property, so XAML cannot bind it. The text is therefore synchronised in code: seeded once
    /// from the view model, then kept in step through the document's change notification.
    /// </para>
    /// </summary>
    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor) return;

        editor.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        editor.SyntaxHighlighting = Ds1LuaHighlighting.Instance(_model.AiNames);
        ApplyEditorPreferences(editor);

        if (_watchedEditors.Add(editor))
        {
            editor.DataContextChanged += OnEditorDataContextChanged;
            editor.TextArea.TextEntered += OnCompletionTextEntered;
            editor.TextArea.TextEntering += OnCompletionTextEntering;
            editor.PreviewKeyDown += OnCompletionKeyDown;
        }

        BindEditor(editor);
    }

    /// <summary>
    /// The TabControl reuses the editor when the next tab is the same document type, so this is the
    /// event that actually fires on a .lua to .lua tab switch.
    /// </summary>
    private void OnEditorDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _completion?.Close();
        if (sender is TextEditor editor) BindEditor(editor);
    }

    /// <summary>Points the editor at whichever document it is now showing.</summary>
    private void BindEditor(TextEditor editor)
    {
        if (editor.DataContext is not LuaEditorViewModel vm)
        {
            UnbindEditor(editor);
            return;
        }

        if (_wiredEditors.TryGetValue(editor, out var existing) && ReferenceEquals(existing.ViewModel, vm))
            return; // already showing this document

        UnbindEditor(editor);

        editor.Text = vm.Text;

        var link = new EditorLink(this, editor, vm);
        link.Attach();
        _wiredEditors[editor] = link;
        UpdateFoldings(editor, capture: false);

        RestoreEditorView(editor, vm);
    }

    /// <summary>
    /// Detaches the editor from the document it was showing and remembers where the caret and the
    /// viewport were, so that coming back to this script puts the user where they left off.
    /// </summary>
    private void UnbindEditor(TextEditor editor)
    {
        if (!_wiredEditors.Remove(editor, out var link)) return;

        CaptureEditorView(editor, link.ViewModel);
        CaptureFoldings(editor, link.ViewModel);
        link.Detach();
        if (_foldingTimers.Remove(editor, out var timer)) timer.Stop();
        if (_foldings.Remove(editor, out var folding)) FoldingManager.Uninstall(folding);
    }

    /// <summary>Fires only when the whole template is torn down, i.e. when switching document types.</summary>
    private void OnEditorUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextEditor editor) return;

        UnbindEditor(editor);
        _completion?.Close();
        if (_watchedEditors.Remove(editor))
        {
            editor.DataContextChanged -= OnEditorDataContextChanged;
            editor.TextArea.TextEntered -= OnCompletionTextEntered;
            editor.TextArea.TextEntering -= OnCompletionTextEntering;
            editor.PreviewKeyDown -= OnCompletionKeyDown;
        }
    }

    private static void CaptureEditorView(TextEditor editor, LuaEditorViewModel vm)
    {
        vm.CaretOffset = editor.CaretOffset;
        vm.ScrollOffsetX = editor.HorizontalOffset;
        vm.ScrollOffsetY = editor.VerticalOffset;
    }

    private static void RestoreEditorView(TextEditor editor, LuaEditorViewModel vm)
    {
        editor.CaretOffset = Math.Clamp(vm.CaretOffset, 0, editor.Document.TextLength);
        editor.ScrollToHorizontalOffset(vm.ScrollOffsetX);
        editor.ScrollToVerticalOffset(vm.ScrollOffsetY);
        editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>
    /// Pushes the goal grid's selection into the view model. WPF offers no way to bind
    /// <c>DataGrid.SelectedItems</c>, and the row commands have to act on the rows the user picked
    /// rather than on every row the filter happens to be showing.
    /// </summary>
    private void OnGoalSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (e.Source is not WpfDataGrid grid) return;
        if (grid.DataContext is not LuaInfoViewModel vm) return;

        // SelectedItems is the non-generic IList, so OfType rather than Cast: it drops the
        // null entries and keeps the nullability annotations honest.
        vm.SetSelectedRows(grid.SelectedItems.Cast<object?>().OfType<GoalRow>());
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Allow "open with" from Explorer and .luainfo file associations.
        var paths = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(a => !a.StartsWith('-'))
            .Where(File.Exists)
            .ToList();

        foreach (var path in paths) _model.Open(path);
    }

    /// <summary>Applies font/line-number preferences to every open editor.</summary>
    private void ApplyEditorPreferences()
    {
        foreach (var editor in FindAllVisualChildren<TextEditor>(this))
            ApplyEditorPreferences(editor);
    }

    private void ApplyEditorPreferences(TextEditor editor)
    {
        editor.FontSize = _settings.EditorFontSize;
        try
        {
            editor.FontFamily = new FontFamily(_settings.EditorFontFamily);
        }
        catch (ArgumentException)
        {
            editor.FontFamily = new FontFamily("Consolas");
        }
        editor.ShowLineNumbers = _settings.ShowLineNumbers;
        editor.WordWrap = _settings.WordWrap;

        // Assigned to TextArea directly, and not mutated on TextEditor.Options, so that TextArea.Options
        // holds a local value instead of the template binding AvalonEdit installs. Clearing a template
        // chain invalidates every template-sourced value on it, and AvalonEdit's handler for that
        // invalidation dereferences a TextView that is already gone - which aborted the tab switch
        // whenever a Lua tab and a goal-table tab traded places.
        editor.TextArea.Options = new TextEditorOptions
        {
            ConvertTabsToSpaces = true,
            IndentationSize = 4,
            EnableHyperlinks = false,
            EnableEmailHyperlinks = false,
        };

        // Caret, selection and current-line live on nested objects rather than the editor itself.
        if (Brush("EditorCaret") is { } caret) editor.TextArea.Caret.CaretBrush = caret;
        if (Brush("EditorSelection") is { } selection) editor.TextArea.SelectionBrush = selection;
        if (Brush("EditorSelectionFg") is { } selectionFg) editor.TextArea.SelectionForeground = selectionFg;
        if (Brush("EditorCurrentLine") is { } line) editor.TextArea.TextView.CurrentLineBackground = line;
    }

    /// <summary>Looks a brush up in the merged theme resources, or null when it is absent.</summary>
    private static Brush? Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush;

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        ThemeManager.Apply(settings);
        ApplyEditorPreferences();
        settings.Save();
        _model.ReplaceSettings(settings);
    }

    private void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } chosen)
            _model.SettingsApplied?.Invoke(chosen);
    }

    private void OnSymbolDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfListBox { SelectedItem: string symbol })
            InsertAtCursor(symbol);
    }

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfListBox { SelectedItem: string path })
            _model.Open(path);
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => FocusFindBox();

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnAboutClick(object sender, RoutedEventArgs e) =>
        DialogService.Info(
            "Dark Souls Lua Studio\n\n" +
            "An editor for Dark Souls PTDE and Dark Souls III AI data.\n\n" +
            "• .lua       - decompiled or hand written Lua 5.0 AI scripts\n" +
            "• .luainfo  - AI goal tables (goal ID, name, interrupt flags)\n" +
            "• .luabnd / .luabnd.dcx - BND3/BND4 binder archives\n" +
            "• .luagnl   - permitted global names\n\n" +
            "File formats come from SoulsFormatsNEXT. Decompilation and compilation\n" +
            "come from ElaDiDu's DSLuaDecompiler (ast-fix branch).\n\n" +
            "Bytecode support: " + SupportSummary());

    private static string SupportSummary() => string.Join(", ",
        new[] { LuaDialect.Lua50, LuaDialect.HavokScript, LuaDialect.Lua53Smash }
            .Select(d => $"{LuaEditorViewModel.DialectLabel(d)} " +
                         (LuaBytecodeService.IsCompilerAvailable(d) ? "compile + decompile" : "decompile only")));

    // ---- find / replace ----------------------------------------------------

    private void OnFindNextClick(object sender, RoutedEventArgs e) => FindNext();
    private void OnReplaceClick(object sender, RoutedEventArgs e) => ReplaceCurrent();
    private void OnReplaceAllClick(object sender, RoutedEventArgs e) => ReplaceAll();

    private void OnFindTermKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FindTermBox.Clear();
            FocusActiveEditor();
            e.Handled = true;
        }
    }

    private void FindNext()
    {
        var editor = FindActiveEditor();
        if (editor is null) return;

        string term = FindTermBox.Text;
        if (term.Length == 0) return;

        var comparison = CurrentComparison();
        var document = editor.Document;
        string text = document.Text;

        int start = Math.Clamp(editor.CaretOffset, 0, text.Length);
        int found = text.IndexOf(term, start, comparison);
        if (found < 0 && start > 0)
            found = text.IndexOf(term, 0, comparison); // wrap around

        if (found < 0)
        {
            FindStatusText.Text = "No matches";
            return;
        }

        editor.Select(found, term.Length);
        editor.ScrollToLine(document.GetLineByOffset(found).LineNumber);
        FindStatusText.Text = $"Line {document.GetLineByOffset(found).LineNumber}";
    }

    private StringComparison CurrentComparison() =>
        MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private void ReplaceCurrent()
    {
        var editor = FindActiveEditor();
        if (editor is null) return;

        if (editor.SelectionLength == 0)
        {
            FindNext();
            return;
        }

        var document = editor.Document;
        string selected = document.GetText(editor.SelectionStart, editor.SelectionLength);

        if (selected.Equals(FindTermBox.Text, CurrentComparison()))
        {
            document.Replace(editor.SelectionStart, editor.SelectionLength, ReplaceTermBox.Text);
        }

        FindNext();
    }

    private void ReplaceAll()
    {
        var editor = FindActiveEditor();
        if (editor is null) return;

        string term = FindTermBox.Text;
        if (term.Length == 0) return;

        var comparison = CurrentComparison();
        string text = editor.Document.Text;
        string replacement = ReplaceTermBox.Text;

        var pieces = new System.Text.StringBuilder(text.Length);
        int index = 0;
        int count = 0;

        while (index <= text.Length - term.Length)
        {
            int found = text.IndexOf(term, index, comparison);
            if (found < 0) break;

            pieces.Append(text, index, found - index).Append(replacement);
            index = found + term.Length;
            count++;
        }

        if (count == 0)
        {
            FindStatusText.Text = "No matches";
            return;
        }

        pieces.Append(text, index, text.Length - index);
        editor.Document.Text = pieces.ToString();
        FindStatusText.Text = $"Replaced {count} occurrence(s)";
    }

    private void FocusFindBox()
    {
        if (FindActiveEditor() is null)
        {
            DialogService.Info("There is no script tab open to search.");
            return;
        }

        FindTermBox.Focus();
        FindTermBox.SelectAll();
    }

    private void FocusActiveEditor() => FindActiveEditor()?.Focus();

    // ---- editor plumbing ---------------------------------------------------

    private void InsertAtCursor(string text)
    {
        var editor = FindActiveEditor();
        if (editor is null)
        {
            DialogService.Info("Open a Lua script tab first, then double click a symbol to insert it.");
            return;
        }

        editor.TextArea.PerformTextInput(text);
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    /// <summary>
    /// Finds the editor belonging to the selected tab.
    /// <para>
    /// This used to walk down from <c>TabControl.SelectedContent</c>, which is a mistake: that is
    /// the tab's <em>data item</em>, and <see cref="DocumentViewModel"/> is a plain INotifyPropertyChanged
    /// object rather than a DependencyObject. The type test therefore failed every time and this
    /// returned null unconditionally, so double clicking a symbol, Ctrl+F, Find Next, Replace and
    /// Replace All all did nothing even with a script tab open and no complaint.
    /// </para>
    /// </summary>
    private TextEditor? FindActiveEditor()
    {
        var wired = _wiredEditors.Keys.FirstOrDefault(e => ReferenceEquals(e.DataContext, _model.SelectedDocument));
        if (wired is not null) return wired;
        var tabControl = FindVisualChild<WpfTabControl>(this);
        if (tabControl?.SelectedItem is not DocumentViewModel selected) return null;

        // Only the selected tab's template is realised, so there is at most one match, and none at
        // all for a goal table or globals tab - which is what those callers expect.
        foreach (var editor in FindAllVisualChildren<TextEditor>(tabControl))
            if (ReferenceEquals(editor.DataContext, selected))
                return editor;

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var found = FindVisualChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Every instance of T in the visual tree, used to restyle all open editors at once.</summary>
    private static IEnumerable<T> FindAllVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in FindAllVisualChildren<T>(child)) yield return nested;
        }
    }
}
