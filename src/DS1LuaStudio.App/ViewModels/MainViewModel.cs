using DS1LuaStudio.App.Mvvm;
using DS1LuaStudio.App.Services;
using DS1LuaStudio.Core;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows;

namespace DS1LuaStudio.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private DocumentViewModel? _selectedDocument;
    private string _status = "Ready.";
    private string _symbolFilter = string.Empty;
    private bool _ignoreDebugInfo;
    private bool _outputDebugComments;
    private bool _catchPassExceptions = true;
    private readonly Dictionary<string, LuabndArchive> _relatedArchives = new(StringComparer.OrdinalIgnoreCase);

    private LuabndArchive? GetArchive(string path)
    {
        path = Path.GetFullPath(path);
        if (_relatedArchives.TryGetValue(path, out var existing)) return existing;
        var archive = LuabndArchive.TryOpen(path);
        if (archive is not null) _relatedArchives[path] = archive;
        return archive;
    }

    public bool NavigateToSymbol(string symbol)
    {
        bool Reveal(LuaEditorViewModel editor)
        {
            int offset = LuaSymbolNavigation.FindDefinition(editor.Text, symbol);
            if (offset < 0) return false;
            editor.CaretOffset = offset;
            SelectedDocument = editor;
            Status = $"{symbol}: {editor.Title}";
            return true;
        }
        if (SelectedDocument is LuaEditorViewModel current && Reveal(current)) return true;
        foreach (var editor in Documents.OfType<LuaEditorViewModel>()) if (Reveal(editor)) return true;
        var active = SelectedDocument as LuaEditorViewModel;
        var archives = new[] { active?.Document.Archive }
            .Concat(_relatedArchives.Values.Where(a => Path.GetFileName(a.FilePath).StartsWith("aicommon.", StringComparison.OrdinalIgnoreCase) && active is not null && string.Equals(
                Path.GetDirectoryName(a.FilePath), GoalSearchDirectory(active.Document), StringComparison.OrdinalIgnoreCase)))
            .Where(a => a is not null).Distinct().ToList();
        // A symbol must occur in the bytecode before we spend time decompiling a candidate.
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(symbol);
        foreach (var archive in archives)
        foreach (var entry in archive!.Entries.Where(e => e.LooksLikeLua && e.Bytes.AsSpan().IndexOf(needle) >= 0 && LuaSymbolNavigation.HasGlobalDefinition(e.Bytes, symbol)))
        {
            var doc = ScriptFileOpener.OpenArchiveEntry(archive, entry.Name, BuildSettings());
            if (doc.DecompileError is not null || LuaSymbolNavigation.FindDefinition(doc.Text, symbol) < 0) continue;
            var editor = new LuaEditorViewModel(doc, BuildSettings());
            Configure(editor);
            Documents.Add(editor);
            return Reveal(editor);
        }
        Status = $"No script definition found for {symbol}. Engine calls are native functions.";
        return false;
    }

    public MainViewModel()
    {
        AiNames = AiNameDatabase.LoadEmbedded();

        OpenFileCommand = new RelayCommand(OpenFileDialog);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        SaveCommand = new RelayCommand(() => SaveActive(), () => CanSaveActive());
        SaveAsCommand = new RelayCommand(SaveActiveAs, () => CanSaveActive());
        CloseCommand = new RelayCommand(CloseActive, () => SelectedDocument is not null);
        CloseAllCommand = new RelayCommand(CloseAll, () => Documents.Count > 0);

        NewLuaScriptCommand = new RelayCommand(NewLuaScript);
        NewLuaGnlCommand = new RelayCommand(() =>
        {
            var globals = new LuaGnlViewModel(LuaGnlDocument.CreateNew(GameMode));
            Documents.Add(globals);
            SelectedDocument = globals;
        });
        // Goals belong to a .luainfo, so creating one is off while a Lua script is being edited.
        NewLuaInfoCommand = new RelayCommand(NewLuaInfo, () => SelectedDocument is not LuaEditorViewModel);

        ReloadFromBytecodeCommand = new RelayCommand(ReloadActive, () => SelectedDocument is LuaEditorViewModel { Document.OriginalBytecode: not null });
        OpenEntryCommand = new RelayCommand(OpenSelectedArchiveEntry, () => SelectedEntryForOpen() is not null);
        ExtractAllCommand = new RelayCommand(ExtractAllFromArchive, () => SelectedDocument is ArchiveViewModel);
        InsertSymbolCommand = new RelayCommand(() => InsertSymbol(null));
        ValidateCommand = new RelayCommand(() => (SelectedDocument as LuaInfoViewModel)?.Validate(), () => SelectedDocument is LuaInfoViewModel);

        AddGoalCommand = new RelayCommand(() => (SelectedDocument as LuaInfoViewModel)?.AddRow(), () => SelectedDocument is LuaInfoViewModel);
        AddGoalPairCommand = new RelayCommand(
            () => (SelectedDocument as LuaInfoViewModel)?.AddGoalPair(),
            () => SelectedDocument is LuaInfoViewModel);
        DuplicateGoalCommand = new RelayCommand(DuplicateGoal, () => SelectedGoal() is not null);
        RevealScriptGoalCommand = new RelayCommand(RevealScriptGoal, () => SelectedDocument is LuaEditorViewModel { HasGoalLink: true });
        DeleteGoalCommand = new RelayCommand(DeleteGoals, () => ActiveInfo?.HasGoalSelection == true);
        SortGoalsCommand = new RelayCommand(() => (SelectedDocument as LuaInfoViewModel)?.SortById(), () => SelectedDocument is LuaInfoViewModel);
        MoveGoalUpCommand = new RelayCommand(() => MoveGoal(-1), () => SelectedGoal() is { } g && g.Index > 0);
        MoveGoalDownCommand = new RelayCommand(() => MoveGoal(1), () => SelectedGoal() is { } g && g.Index < ActiveInfo!.Goals.Count - 1);

        ExportGoalsCommand = new RelayCommand(ExportGoals, () => SelectedDocument is LuaInfoViewModel);
        ImportGoalsCommand = new RelayCommand(ImportGoals, () => SelectedDocument is LuaInfoViewModel);
        RequestFindCommand = new RelayCommand(RequestFind);

        RefreshSymbols();
    }

    /// <summary>Lets the view refresh the status line after the editor content changed.</summary>
    public void UpdateStatus() => OnPropertyChanged(nameof(ActiveDetail));

    public AiNameDatabase AiNames { get; private set; }
    public IReadOnlyList<GameMode> GameModes { get; } = Enum.GetValues<GameMode>();
    public GameMode GameMode
    {
        get => Settings.GameMode;
        set
        {
            if (Settings.GameMode == value) return;
            Settings.GameMode = value;
            RefreshMode();
            Settings.Save();
        }
    }
    public bool EnableAutocomplete
    {
        get => Settings.EnableAutocomplete;
        set { Settings.EnableAutocomplete = value; OnPropertyChanged(); Settings.Save(); }
    }
    public string ModeCaption => $"{GameMode.DisplayName()} — {GameMode.SizeTSize() * 8}-bit AI";
    public string SymbolsCaption => $"{GameMode} AI SYMBOLS";
    private void RefreshMode()
    {
        AiNames = AiNameDatabase.LoadEmbedded(GameMode);
        Raise(nameof(GameMode), nameof(AiNames), nameof(ModeCaption), nameof(SymbolsCaption), nameof(EnableAutocomplete));
        RefreshSymbols();
        // Existing compiled scripts keep the width of their own chunk across mode switches.
        foreach (var editor in Documents.OfType<LuaEditorViewModel>()) ConfigureOutput(editor);
    }
    private void ConfigureOutput(LuaEditorViewModel editor)
    {
        editor.TargetSizeT = editor.Document.SizeTSize != 0 ? editor.Document.SizeTSize
            : GameMode == GameMode.DS3 ? 8 : Settings.CompileLua50As32Bit ? 4 : 8;
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = new();

    public ObservableCollection<string> RecentFiles { get; } = new();

    public DocumentViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!Set(ref _selectedDocument, value)) return;
            Raise(nameof(ActiveIsEditor), nameof(ActiveIsLuaInfo), nameof(ActiveIsArchive),
                   nameof(ActiveDetail), nameof(HasDocument));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasDocument => SelectedDocument is not null;
    public bool ActiveIsEditor => SelectedDocument is LuaEditorViewModel;
    public bool ActiveIsLuaInfo => SelectedDocument is LuaInfoViewModel;
    public bool ActiveIsArchive => SelectedDocument is ArchiveViewModel;
    public string ActiveDetail => SelectedDocument?.StatusText ?? string.Empty;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    // ---- decompile options -------------------------------------------------

    public bool IgnoreDebugInfo
    {
        get => _ignoreDebugInfo;
        set { if (Set(ref _ignoreDebugInfo, value)) ApplySettings(); }
    }

    public bool OutputDebugComments
    {
        get => _outputDebugComments;
        set { if (Set(ref _outputDebugComments, value)) ApplySettings(); }
    }

    public bool CatchPassExceptions
    {
        get => _catchPassExceptions;
        set { if (Set(ref _catchPassExceptions, value)) ApplySettings(); }
    }

    /// <summary>
    /// Emit Lua 5.0 bytecode with a 32-bit size_t so Dark Souls PTDE can load it.
    /// </summary>
    public bool CompileLua50As32Bit
    {
        get => Settings.CompileLua50As32Bit;
        set
        {
            if (Settings.CompileLua50As32Bit == value) return;
            Settings.CompileLua50As32Bit = value;
            ApplyCompileSizeT();
        }
    }

    /// <summary>Shared appearance/output preferences; owned by the view so it can persist them.</summary>
    public AppSettings Settings { get; set; } = new();

    /// <summary>
    /// Replace generated argument names (<c>f1_arg0</c>) with the real AI callback parameters using
    /// the goal tables discovered next to the script.
    /// </summary>
    public bool NameAiArgumentsFromGoals
    {
        get => Settings.NameAiArgumentsFromGoals;
        set
        {
            if (Settings.NameAiArgumentsFromGoals == value) return;
            Settings.NameAiArgumentsFromGoals = value;
            ReapplyAiNaming();
        }
    }

    /// <summary>Relinks every open script against the goal tables found near it.</summary>
    public void ReapplyAiNaming()
    {
        if (Documents.Count == 0) return; // nothing to relink; leave the greeting alone

        int stale = 0;

        foreach (var editor in Documents.OfType<LuaEditorViewModel>())
        {
            AttachAiNaming(editor);

            // Turning naming off cannot be undone by re-running the namer, so the only faithful way
            // back to the raw decompiler output is the original bytecode. Doing that to a script
            // the user has edited would throw their work away, so it is left to Tools -> Reload.
            if (!Settings.NameAiArgumentsFromGoals &&
                editor.Document.OriginalBytecode is not null &&
                editor.IsDirty)
            {
                stale++;
            }
        }

        Status = stale > 0
            ? $"AI argument naming disabled. {stale} edited script(s) still show the named arguments; " +
              "use Tools -> Reload from bytecode to see the raw decompiler output."
            : Settings.NameAiArgumentsFromGoals
                ? "AI argument naming enabled."
                : "AI argument naming disabled; generated f1_arg0 names kept.";

        // The goal link follows the catalog, so it has to be re-enabled here too.
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Folder to search for goal tables: the script's own folder, or the folder holding the archive
    /// the script was extracted from.
    /// </summary>
    private static string? GoalSearchDirectory(LuaScriptDocument script)
    {
        string? owner = script.FilePath;
        if (string.IsNullOrEmpty(owner) && script.Archive?.FilePath is { Length: > 0 } archive)
            owner = archive;

        return string.IsNullOrEmpty(owner) ? null : Path.GetDirectoryName(Path.GetFullPath(owner));
    }

    /// <summary>
    /// Gives a script the goal table next to it: its callback arguments get their real names, and the
    /// catalog is kept so the names can be re-applied after a reload.
    /// </summary>
    private void AttachAiNaming(LuaEditorViewModel editor)
    {
        editor.NamingApplier = source => Settings.NameAiArgumentsFromGoals && editor.Catalog.GoalCount > 0
            ? Ds1ArgumentNamer.Apply(source, editor.Catalog).Source
            : source;

        var nearby = GoalSearchDirectory(editor.Document) is { } directory
            ? Ds1AiCatalog.DiscoverNearDirectory(directory, mode: editor.Document.SizeTSize == 8 ? GameMode.DS3 : editor.Document.SizeTSize == 4 ? GameMode.DS1 : GameMode)
            : Ds1AiCatalog.Empty;
        var archiveCatalog = editor.Document.Archive is { } owner
            ? Ds1AiCatalog.FromArchive(owner) : Ds1AiCatalog.Empty;
        var commonCatalog = Ds1AiCatalog.Empty;
        if (GoalSearchDirectory(editor.Document) is { } folder)
        {
            var commonPath = Path.Combine(folder, "aicommon.luabnd.dcx");
            var common = Documents.OfType<ArchiveViewModel>().Select(a => a.Archive)
                .FirstOrDefault(a => string.Equals(a.FilePath, commonPath, StringComparison.OrdinalIgnoreCase))
                ?? (File.Exists(commonPath) ? GetArchive(commonPath) : null);
            if (common is not null) commonCatalog = Ds1AiCatalog.FromArchive(common);
        }
        var openTables = Documents.OfType<LuaInfoViewModel>().Where(v =>
            editor.Document.Archive is { } a ? ReferenceEquals(v.Document.Archive, a)
                : v.Document.FilePath is { } p && string.Equals(Path.GetDirectoryName(p), GoalSearchDirectory(editor.Document), StringComparison.OrdinalIgnoreCase));
        var catalog = Ds1AiCatalog.Combine(Ds1AiCatalog.FromDocuments(openTables.Select(v => v.Document)), archiveCatalog, nearby, commonCatalog);

        // Renaming is presentational, and ApplyAiNaming clears the dirty marker, so a script that
        // was already edited would stop looking unsaved.
        bool wasDirty = editor.IsDirty;

        editor.SetCatalog(catalog);
        if (Settings.NameAiArgumentsFromGoals && !wasDirty) editor.ApplyAiNaming(catalog);
        if (wasDirty) editor.IsDirty = true;
    }

    /// <summary>
    /// Follows the goal link on an open battle/logic script: opens the goal table beside the script and
    /// selects that script's own goal row.
    /// </summary>
    private void RevealScriptGoal()
    {
        if (SelectedDocument is not LuaEditorViewModel { GoalLink: { Exists: true } link }) return;

        // The table the arguments were named from is the one to jump to.
        if (SelectedDocument is not LuaEditorViewModel editor) return;
        var target = editor.Catalog.FindGoal(link.Name);
        if (target?.Archive is { } archive && target.EntryName is { } entryName)
        {
            var table = Documents.OfType<LuaInfoViewModel>().FirstOrDefault(v => ReferenceEquals(v.Document.Archive, archive) && v.Document.EntryName == entryName);
            if (table is null) { table = new LuaInfoViewModel(LuaInfoDocument.FromArchive(archive, entryName)); Documents.Add(table); }
            SelectedDocument = table;
            var goalRow = table.Goals.FirstOrDefault(g => g.Name == link.Name);
            if (goalRow is not null) table.RevealRow(goalRow);
            return;
        }
        if (target?.SourcePath is not { } path) return;

        var vm = Documents.OfType<LuaInfoViewModel>()
            .FirstOrDefault(v => string.Equals(v.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));

        if (vm is null)
        {
            if (!Open(path, quiet: true)) return;
            if (SelectedDocument is not LuaInfoViewModel opened) return;
            vm = opened;
        }
        else
        {
            SelectedDocument = vm;
        }

        var (battle, logic) = vm.Document.FindPair(link.GoalId);
        var row = link.Identity.Kind == AiScriptKind.Logic ? logic : battle;

        if (row is null)
        {
            Status = $"{link.Caption} is not in {Path.GetFileName(path)}.";
            return;
        }

        vm.RevealRow(row);
        vm.Validate();
        Status = $"{link.Caption}: {row.Name}";
    }

    private void ApplyCompileSizeT()
    {
        int touched = 0;
        foreach (var editor in Documents.OfType<LuaEditorViewModel>())
        {
            ConfigureOutput(editor);
            touched++;
        }

        // At startup there is nothing open, and saying so here would replace the "Ready." greeting.
        if (touched == 0) return;

        Status = $"{ModeCaption}. Existing bytecode keeps its original width.";
    }

    /// <summary>Replaces the preferences after the appearance dialog was applied.</summary>
    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings;
        RefreshMode();

        // The sidebar checkboxes read straight off Settings, so they need telling, or they keep
        // showing the value the dialog just replaced.
        Raise(nameof(NameAiArgumentsFromGoals), nameof(CompileLua50As32Bit));

        ApplyCompileSizeT();

        // This used to stop here, which left "Name AI arguments from .luainfo" in the appearance
        // dialog doing nothing to the scripts that were already open.
        ReapplyAiNaming();
    }

    public DecompileSettings BuildSettings() => new()
    {
        IgnoreDebugInfo = IgnoreDebugInfo,
        OutputDebugComments = OutputDebugComments,
        CatchPassExceptions = CatchPassExceptions,
    };

    private void ApplySettings()
    {
        var settings = BuildSettings();
        foreach (var doc in Documents.OfType<LuaEditorViewModel>())
            doc.Settings = settings;
        Status = "Decompile options updated.";
    }

    // ---- AI symbol palette -------------------------------------------------

    public string SymbolFilter
    {
        get => _symbolFilter;
        set
        {
            if (!Set(ref _symbolFilter, value)) return;
            RefreshSymbols();
        }
    }

    public ObservableCollection<string> Symbols { get; } = new();

    private void RefreshSymbols()
    {
        Symbols.Clear();
        foreach (var s in AiNames.KnownSymbols.OrderBy(s => s, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(SymbolFilter) ||
                s.Contains(SymbolFilter, StringComparison.OrdinalIgnoreCase))
            {
                Symbols.Add(s);
            }
        }
        OnPropertyChanged(nameof(SymbolHint));
    }

    public string SymbolHint => string.IsNullOrWhiteSpace(SymbolFilter)
        ? $"{Symbols.Count} AI symbols - double click to insert"
        : $"{Symbols.Count} of {AiNames.KnownSymbols.Count} symbols match";

    // ---- commands ----------------------------------------------------------

    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand CloseAllCommand { get; }
    public RelayCommand NewLuaScriptCommand { get; }
    public RelayCommand NewLuaGnlCommand { get; }
    public RelayCommand NewLuaInfoCommand { get; }
    public RelayCommand ReloadFromBytecodeCommand { get; }
    public RelayCommand OpenEntryCommand { get; }
    public RelayCommand ExtractAllCommand { get; }
    public RelayCommand InsertSymbolCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand AddGoalCommand { get; }
    public RelayCommand AddGoalPairCommand { get; }

    /// <summary>Jumps from an open script to its own goal row in the goal table.</summary>
    public RelayCommand RevealScriptGoalCommand { get; }
    public RelayCommand DuplicateGoalCommand { get; }
    public RelayCommand DeleteGoalCommand { get; }
    public RelayCommand SortGoalsCommand { get; }
    public RelayCommand MoveGoalUpCommand { get; }
    public RelayCommand MoveGoalDownCommand { get; }
    public RelayCommand ExportGoalsCommand { get; }
    public RelayCommand ImportGoalsCommand { get; }
    public RelayCommand RequestFindCommand { get; }

    /// <summary>Set by the view so symbols can be dropped into the active editor.</summary>
    public Action<string>? TextInsertRequested { get; set; }

    /// <summary>Set by the view so the editor can be focused for searching.</summary>
    public Action? FindRequested { get; set; }

    /// <summary>Set by the view so appearance changes can be applied and persisted.</summary>
    public Action<AppSettings>? SettingsApplied { get; set; }

    public void RequestFind() => FindRequested?.Invoke();

    public bool CanInsertDraft(string source)
    {
        if (SelectedDocument is not LuaEditorViewModel editor) return false;
        var existing = LuaFunctionDrafts.FindFunctions(editor.Text).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var duplicate = LuaFunctionDrafts.FindFunctions(source).Select(f => f.Name).FirstOrDefault(existing.Contains);
        if (duplicate is null) return true;
        Status = $"Function '{duplicate}' already exists. Rename it in the library preview before inserting.";
        return false;
    }

    public void OpenDraftScript(string source, GameMode mode)
    {
        var document = LuaScriptDocument.CreateNewText("draft.lua");
        document.Text = source;
        var editor = NewEditor(() => new LuaEditorViewModel(document, BuildSettings()));
        editor.TargetSizeT = mode.SizeTSize();
        editor.IsDirty = true;
        Documents.Add(editor);
        SelectedDocument = editor;
        Status = $"Opened a {mode} draft as a new script.";
    }

    private bool CanSaveActive() => SelectedDocument?.CanSave == true;

    // ---- opening -----------------------------------------------------------

    private void OpenFileDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Lua or AI file",
            Filter = "Supported files|*.lua;*.luainfo;*.luagnl;*.luabnd;*.luac;*.hks;*.bnd;*.dcx|" +
                     "Lua source (*.lua)|*.lua|" +
                     "AI goal tables (*.luainfo)|*.luainfo|" +
                     "AI archives (*.luabnd;*.bnd;*.dcx)|*.luabnd;*.bnd;*.dcx|" +
                     "Bytecode (*.luac;*.hks)|*.luac;*.hks|" +
                     "All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() == true)
            foreach (var path in dialog.FileNames)
                Open(path);
    }

    private void OpenFolder()
    {
        var folder = Services.FolderPicker.PickFolder("Choose a folder with AI scripts or goal tables");
        if (folder is null) return;

        var interesting = new[] { ".luainfo", ".luabnd", ".luagnl", ".lua" };
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => interesting.Contains(Path.GetExtension(f.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase) ? f[..^4] : f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        if (files.Count == 0)
        {
            Status = "No supported files found in that folder.";
            return;
        }

        foreach (var file in files) Open(file, quiet: true);
        Status = $"Opened {files.Count} file(s) from {Path.GetFileName(folder)}.";
    }

    public bool Open(string path, bool quiet = false)
    {
        try
        {
            var existing = Documents.FirstOrDefault(d => d switch
            {
                ArchiveViewModel a => string.Equals(a.Archive.FilePath, path, StringComparison.OrdinalIgnoreCase),
                LuaInfoViewModel i => string.Equals(i.Document.FilePath, path, StringComparison.OrdinalIgnoreCase),
                LuaGnlViewModel g => string.Equals(g.Document.FilePath, path, StringComparison.OrdinalIgnoreCase),
                LuaEditorViewModel e => string.Equals(e.Document.FilePath, path, StringComparison.OrdinalIgnoreCase),
                _ => false,
            });
            if (existing is not null) { SelectedDocument = existing; return true; }
            var doc = CreateDocument(path);
            if (doc is null)
            {
                if (!quiet) Status = $"Could not open {Path.GetFileName(path)}.";
                return false;
            }

            Documents.Add(doc);
            SelectedDocument = doc;
            NoteRecent(path);
            if (!quiet) Status = Opened_Description(doc);
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed to open {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }
    }

    private DocumentViewModel? CreateDocument(string path)
    {
        switch (ScriptFileOpener.Classify(path))
        {
            case ScriptFileKind.LuaInfo:
                return CreateLuaInfo(path);

            case ScriptFileKind.LuaGnl:
                return new LuaGnlViewModel(LoadGnl(path));

            case ScriptFileKind.LuabndArchive:
                return CreateArchive(path);

            case ScriptFileKind.LuaSource:
                return NewEditor(() => new LuaEditorViewModel(LuaScriptDocument.FromTextFile(path), BuildSettings()));

            case ScriptFileKind.LuaBytecode:
                return NewEditor(() => new LuaEditorViewModel(
                    ScriptFileOpener.OpenBytecode(path, BuildSettings()), BuildSettings()));

            default:
                return null;
        }
    }

    /// <summary>Creates an editor and, when enabled, links it to the nearby goal tables.</summary>
    private LuaEditorViewModel NewEditor(Func<LuaEditorViewModel> factory)
    {
        var editor = factory();
        Configure(editor);
        return editor;
    }

    /// <summary>
    /// Attaches the hooks every editor needs. Done in one place so a document created from a new
    /// file, from bytecode, or out of an archive cannot end up missing one of them.
    /// </summary>
    private void Configure(LuaEditorViewModel editor)
    {
        editor.SaveFormatChooser = AskSaveFormat;
        ConfigureOutput(editor);
        AttachAiNaming(editor);
    }

    /// <summary>
    /// Shows the compile / save source / both prompt for a script that is only source text, and
    /// returns null when the user cancels.
    /// </summary>
    private SaveFormat? AskSaveFormat(string path)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;

        var dialog = new Views.SaveFormatWindow(path) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private static LuaInfoViewModel CreateLuaInfo(string path)
    {
        var doc = new LuaInfoDocument();
        doc.Load(path);
        var vm = new LuaInfoViewModel(doc);
        vm.Validate();
        return vm;
    }

    private static LuaGnlDocument LoadGnl(string path)
    {
        var doc = new LuaGnlDocument();
        doc.Load(path);
        return doc;
    }

    private DocumentViewModel? CreateArchive(string path)
    {
        var archive = GetArchive(path);
        if (archive is null)
        {
            Status = $"{Path.GetFileName(path)} is not a readable binder archive.";
            return null;
        }
        return new ArchiveViewModel(archive);
    }

    private static string Opened_Description(DocumentViewModel doc) => doc switch
    {
        LuaInfoViewModel i => $"Loaded {i.GoalCount} AI goals from {i.Title}.",
        ArchiveViewModel a => $"Loaded {a.ScriptCount} Lua scripts from {a.Title}.",
        LuaEditorViewModel e => $"Opened {e.Title} ({LuaEditorViewModel.DialectLabel(e.Dialect)}).",
        _ => $"Opened {doc.Title}.",
    };

    // ---- new documents -----------------------------------------------------

    private void NewLuaScript()
    {
        var doc = new LuaEditorViewModel(LuaScriptDocument.CreateNewText(), BuildSettings());
        Configure(doc);
        Documents.Add(doc);
        SelectedDocument = doc;
        Status = "Created a new Lua 5.0 script.";
    }

    private void NewLuaInfo()
    {
        var vm = new LuaInfoViewModel(LuaInfoDocument.CreateNew(GameMode));
        Documents.Add(vm);
        SelectedDocument = vm;
        Status = $"Created a new {GameMode}-format goal table.";
    }

    // ---- saving ------------------------------------------------------------

    private void SaveActive()
    {
        if (SelectedDocument is null) return;
        try
        {
            Status = SelectedDocument.Save();
            var saveStatus = Status;
            foreach (var archive in Documents.OfType<ArchiveViewModel>()) archive.Refresh();
            ReapplyAiNaming();
            Status = saveStatus;
            OnPropertyChanged(nameof(ActiveDetail));
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex)
        {
            Status = "Save failed: " + ex.Message;
        }
    }

    private void SaveActiveAs()
    {
        if (SelectedDocument is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save As",
            FileName = SelectedDocument.Title,
            Filter = SelectedDocument is LuaInfoViewModel ? "AI goal table (*.luainfo)|*.luainfo|All files (*.*)|*.*"
                        : SelectedDocument is LuaGnlViewModel ? "Globals (*.luagnl)|*.luagnl|All files (*.*)|*.*"
                        : SelectedDocument is ArchiveViewModel ? "Binder archive (*.luabnd)|*.luabnd|All files (*.*)|*.*"
                        : "Bytecode (*.luac)|*.luac|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                SelectedDocument.SaveAs(dialog.FileName);
                Status = $"Saved {Path.GetFileName(dialog.FileName)}.";
                OnPropertyChanged(nameof(ActiveDetail));
            }
            catch (Exception ex)
            {
                Status = "Save failed: " + ex.Message;
            }
        }
    }

    private void CloseActive()
    {
        if (SelectedDocument is not null) Close(SelectedDocument);
    }

    private void CloseAll()
    {
        foreach (var doc in Documents.ToList()) Close(doc);
    }

    public void Close(DocumentViewModel doc)
    {
        // The old code ran the prompt but then removed the tab whatever the answer was, so saying
        // No still threw the edits away. Anything but Yes has to keep the tab open.
        if (doc.IsDirty && !Services.DialogService.Confirm(
                $"'{doc.Title}' has unsaved changes. Close it and lose them?"))
            return;

        Documents.Remove(doc);
        if (SelectedDocument is not null && !Documents.Contains(SelectedDocument))
            SelectedDocument = Documents.LastOrDefault();
        Status = $"Closed {doc.Title}.";
    }

    // ---- editor actions ----------------------------------------------------

    private void ReloadActive()
    {
        if (SelectedDocument is not LuaEditorViewModel editor) return;
        if (!Services.DialogService.Confirm("Re-run the decompiler? Your edits to this script will be lost."))
            return;

        editor.ReloadFromBytecode();
        Status = "Re-decompiled from bytecode.";
    }

    private ArchiveEntry? SelectedEntryForOpen() =>
        (SelectedDocument as ArchiveViewModel)?.SelectedEntry is { CanOpen: true } entry ? entry : null;

    private void OpenSelectedArchiveEntry()
    {
        if (SelectedDocument is not ArchiveViewModel archiveVm) return;
        if (archiveVm.SelectedEntry is not { CanOpen: true } entry) return;

        try
        {
            var existing = Documents.FirstOrDefault(d => d switch
            {
                LuaInfoViewModel i => ReferenceEquals(i.Document.Archive, archiveVm.Archive) && i.Document.EntryName == entry.Name,
                LuaGnlViewModel g => ReferenceEquals(g.Document.Archive, archiveVm.Archive) && g.Document.EntryName == entry.Name,
                LuaEditorViewModel e => ReferenceEquals(e.Document.Archive, archiveVm.Archive) && e.Document.EntryName == entry.Name,
                _ => false,
            });
            if (existing is not null) { SelectedDocument = existing; return; }
            if (entry.LooksLikeLuaInfo || entry.LooksLikeLuaGnl)
            {
                DocumentViewModel table = entry.LooksLikeLuaInfo
                    ? new LuaInfoViewModel(LuaInfoDocument.FromArchive(archiveVm.Archive, entry.Name))
                    : new LuaGnlViewModel(LuaGnlDocument.FromArchive(archiveVm.Archive, entry.Name));
                Documents.Add(table);
                SelectedDocument = table;
                Status = $"Opened {entry.Kind}: {entry.Name}.";
                return;
            }
            var doc = new LuaEditorViewModel(
                ScriptFileOpener.OpenArchiveEntry(archiveVm.Archive, entry.Name, BuildSettings()),
                BuildSettings());
            Configure(doc);

            Documents.Add(doc);
            SelectedDocument = doc;
            Status = $"Decompiled '{entry.Name}' from {archiveVm.Title}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not decompile '{entry.Name}': {ex.Message}";
        }
    }

    private void ExtractAllFromArchive()
    {
        if (SelectedDocument is not ArchiveViewModel archiveVm) return;

        var folder = Services.FolderPicker.PickFolder("Choose where to write the decompiled .lua files");
        if (folder is null) return;

        try
        {
            var written = ScriptFileOpener.ExtractAllScripts(archiveVm.Archive, folder, BuildSettings());
            Status = written.Count == 0
                ? "That archive contains no decompilable Lua scripts."
                : $"Wrote {written.Count} .lua file(s) to {folder}.";
        }
        catch (Exception ex)
        {
            Status = "Extraction failed: " + ex.Message;
        }
    }

    private void InsertSymbol(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return;
        TextInsertRequested?.Invoke(symbol);
    }

    // ---- goal table actions ------------------------------------------------

    private LuaInfoViewModel? ActiveInfo => SelectedDocument as LuaInfoViewModel;

    /// <summary>
    /// The rows the grid has selected, which is what the row commands act on. This used to return
    /// every row the filter was showing, which turned a click on Delete into "delete the whole
    /// goal table". Falls back to the single highlighted row so the buttons can never be live with
    /// nothing to act on.
    /// </summary>
    private IEnumerable<GoalRow> SelectedGoals()
    {
        if (ActiveInfo is not { } info) return Enumerable.Empty<GoalRow>();
        if (info.SelectedRows.Count > 0) return info.SelectedRows.ToList();
        return info.SelectedRow is { } single ? new[] { single } : Enumerable.Empty<GoalRow>();
    }

    /// <summary>
    /// The single highlighted row, or null when nothing is selected. Deliberately has no fallback
    /// to the first goal: the old one meant Duplicate and Move silently acted on row 1 whenever the
    /// grid had no selection, and on a row the filter was hiding.
    /// </summary>
    private GoalRow? SelectedGoal() => ActiveInfo?.SelectedRow;

    private void DuplicateGoal()
    {
        var info = ActiveInfo;
        if (info is null) return;
        if (SelectedGoal() is not { } goal) return;

        info.DuplicateRow(goal);
        Status = $"Duplicated '{goal.Name}' (ID {goal.Id}).";
        CommandManager.InvalidateRequerySuggested();
    }

    private void DeleteGoals()
    {
        var rows = SelectedGoals().ToList();
        if (rows.Count == 0) return;

        ActiveInfo?.RemoveRows(rows);
        // These row actions used to report nothing at all, so a click that did the wrong thing was
        // indistinguishable from a click that did nothing.
        Status = rows.Count == 1
            ? $"Deleted goal '{rows[0].Name}' (ID {rows[0].Id})."
            : $"Deleted {rows.Count} goals.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void MoveGoal(int delta)
    {
        var info = ActiveInfo;
        if (info is null) return;
        if (SelectedGoal() is not { } goal) return;

        info.MoveRow(goal, delta);
        Status = $"Moved '{goal.Name}' to row {goal.Index + 1}.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExportGoals()
    {
        if (ActiveInfo is not { } info) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export goals",
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            FileName = Path.GetFileNameWithoutExtension(info.Title) + ".csv",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName,
                dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? info.Document.ToJson()
                    : info.Document.ToCsv(),
                System.Text.Encoding.UTF8);
            Status = $"Exported goals to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            Status = "Export failed: " + ex.Message;
        }
    }

    private void ImportGoals()
    {
        if (ActiveInfo is not { } info) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import goals",
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            var restored = dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? LuaInfoDocument.FromJson(text)
                : LuaInfoDocument.FromCsv(text);

            info.Document.Goals.Clear();
            foreach (var row in restored.Goals.ToList()) info.Document.AddGoal(row);
            info.Document.Reindex();
            info.IsDirty = true;
            info.Validate();
            Status = $"Imported {info.GoalCount} goals from {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            Status = "Import failed: " + ex.Message;
        }
    }

    private void NoteRecent(string path)
    {
        RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 12) RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }
}
