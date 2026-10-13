using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using DS1LuaStudio.Core;
using DS1LuaStudio.App.Mvvm;

namespace DS1LuaStudio.App.ViewModels;

public abstract class DocumentViewModel : ObservableObject
{
    private string _title = "Untitled";
    private bool _isDirty;

    public string Title
    {
        get => _title;
        set { if (Set(ref _title, value)) Raise(nameof(Header)); }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set { if (Set(ref _isDirty, value)) Raise(nameof(Header)); }
    }

    /// <summary>Tab caption including the dirty marker.</summary>
    public string Header => IsDirty ? Title + " *" : Title;

    /// <summary>Short origin/format line shown in the status bar.</summary>
    public abstract string Detail { get; }

    /// <summary>Status bar caption for this document.</summary>
    public virtual string StatusText => Detail;

    /// <summary>Writes the document back to its origin. Returns a short description of what happened.</summary>
    public abstract string Save();

    /// <summary>Writes the document to a new location.</summary>
    public abstract void SaveAs(string path);

    /// <summary>True when this kind of document can be saved straight to disk.</summary>
    public virtual bool CanSave => true;
}

/// <summary>What a script that is only source text should be written out as.</summary>
public enum SaveFormat
{
    /// <summary>Compile the text to bytecode and write it to the document's own path.</summary>
    Compiled,

    /// <summary>Write the edited text back as a source file.</summary>
    Source,

    /// <summary>Write the source, with a compiled copy beside it.</summary>
    Both,
}

/// <summary>A decompiled or hand-written Lua script.</summary>
public sealed class LuaEditorViewModel : DocumentViewModel
{
    private string _text = string.Empty;
    private DecompileSettings _settings = new();

    public LuaScriptDocument Document { get; }

    public LuaEditorViewModel(LuaScriptDocument document, DecompileSettings settings)
    {
        Document = document;
        _text = document.Text;
        _settings = settings;
        Title = document.Title;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            IsDirty = true;
            Raise(nameof(LineCount), nameof(StatusText));
        }
    }

    public DecompileSettings Settings
    {
        get => _settings;
        set => Set(ref _settings, value);
    }

    public LuaDialect Dialect => Document.Dialect;

    public bool CanCompile => Document.DecompileError is null && LuaBytecodeService.IsCompilerAvailable(Dialect);

    /// <summary>Goal tables linked to this script, for the header line.</summary>
    public int LinkedGoals { get; private set; }

    /// <summary>Where those goal tables came from.</summary>
    public string LinkedGoalSource { get; private set; } = string.Empty;

    /// <summary>How many callback parameters were given their real names.</summary>
    public int RenamedParameters { get; private set; }

    /// <summary>Applies the real AI parameter names using a discovered goal table.</summary>
    public void ApplyAiNaming(Ds1AiCatalog catalog)
    {
        if (Dialect != LuaDialect.Lua50 || catalog.GoalCount == 0) return;

        var result = Ds1ArgumentNamer.Apply(Text, catalog);
        if (result.RenamedIdentifiers > 0)
        {
            Document.Text = result.Source;
            Text = result.Source;
            IsDirty = false; // the edit is presentational, not a user change
        }

        LinkedGoals = catalog.GoalCount;
        LinkedGoalSource = string.Join(", ", catalog.Sources.Select(Path.GetFileName));
        RenamedParameters = result.RenamedIdentifiers;

        Raise(nameof(Header), nameof(StatusText), nameof(GoalLinkSummary));
    }

    /// <summary>Empty when nothing was linked, otherwise a short description.</summary>
    public string GoalLinkSummary => LinkedGoals == 0
        ? string.Empty
        : $"{LinkedGoals} goals linked ({LinkedGoalSource})   |   {RenamedParameters} arguments named";

    // ---- the goal table this script belongs to ----------------------------------

    /// <summary>The link to this script's own goal row, or null when the table has no such row.</summary>
    public ScriptGoalLink? GoalLink { get; private set; }

    public bool HasGoalLink => GoalLink is not null;

    /// <summary>
    /// The goal tables found next to this script. Kept so the argument names can be re-applied after
    /// the script is reloaded from its bytecode, and so the goal row can be looked up again.
    /// </summary>
    public Ds1AiCatalog Catalog { get; private set; } = Ds1AiCatalog.Empty;

    /// <summary>Records the goal tables discovered for this script.</summary>
    public void SetCatalog(Ds1AiCatalog catalog)
    {
        Catalog = catalog;

        // A script pulled out of a .luabnd has no path of its own, so its entry name is the fallback.
        var identity = Ds1ScriptIdentity.TryParse(Document.FilePath)
                       ?? Ds1ScriptIdentity.TryParse(Document.EntryName);

        GoalLink = identity is null
            ? null
            : ScriptGoalLink.From(identity, catalog.FindPair(identity.GoalId));

        Raise(nameof(HasGoalLink), nameof(GoalLink));
    }

    // ---- size_t output layout ----------------------------------------------

    public int? TargetSizeT
    {
        get => Document.TargetSizeT;
        set
        {
            if (Document.TargetSizeT == value) return;
            Document.TargetSizeT = value;
            Raise(nameof(RecompileChangesChunkLayout), nameof(WarningSuffix), nameof(SizeTLayout), nameof(StatusText));
        }
    }

    /// <summary>Applies the Lua 5.0 output layout preference: 32-bit size_t for PTDE, or null.</summary>
    public void ApplyCompileSizeTPreference(bool compileAs32Bit) => TargetSizeT = compileAs32Bit ? 4 : null;

    public bool RecompileChangesChunkLayout => Document.RecompileChangesChunkLayout;

    /// <summary>Human readable size_t layout of the document and of a recompile.</summary>
    public string SizeTLayout
    {
        get
        {
            if (Document.SizeTSize == 0) return string.Empty;
            string original = $"{Document.SizeTSize * 8}-bit";
            string output = $"{Document.EffectiveSizeT * 8}-bit";
            return Document.RecompileChangesChunkLayout ? $"size_t {original} -> {output}" : $"size_t {original}";
        }
    }

    /// <summary>Appended to the save message when the chunk layout will not match the game's.</summary>
    public string WarningSuffix =>
        Document.RecompileWarning.Length > 0 ? "  " + Document.RecompileWarning : string.Empty;

    // ---- presentation ------------------------------------------------------

    public static string DialectLabel(LuaDialect d) => d switch
    {
        LuaDialect.Lua50 => "Lua 5.0",
        LuaDialect.HavokScript => "Havok Script",
        LuaDialect.Lua53Smash => "Lua 5.3",
        _ => "Unknown",
    };

    public int LineCount => Text.Length == 0 ? 0 : Text.Count(c => c == '\n') + 1;

    public override string Detail => Document.Origin;

    public override string StatusText =>
        $"{Document.Origin}   |   {DialectLabel(Dialect)}   |   {Document.Encoding.WebName}   |   {LineCount} lines" +
        (SizeTLayout.Length > 0 ? $"   |   {SizeTLayout}" : string.Empty) +
        (GoalLinkSummary.Length > 0 ? $"   |   {GoalLinkSummary}" : string.Empty) +
        (Document.DecompileError is not null ? $"   |   Decompile failed: {Document.DecompileError}" :
            Document.WarningFunctions.Count > 0 ? $"   |   {Document.WarningFunctions.Count} function(s) with decompiler warnings" : string.Empty);

    // ---- saving ------------------------------------------------------------

    public override string Save()
    {
        Document.Text = Text;

        if (Document.IsFromArchive)
        {
            Document.SaveCompiledToArchive();
            FinishSave();
            return $"Compiled and wrote '{Document.EntryName}' back into " +
                   $"{Path.GetFileName(Document.Archive!.FilePath)}." + WarningSuffix;
        }

        var path = Document.FilePath;

        // Nothing sits behind this document except text, so what gets written has to be a choice:
        // compiling over a hand written .lua would replace the source with binary and lose it.
        if (Document.OriginalBytecode is null)
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("This script has never been saved; use Save As.");

            if (!File.Exists(path))
                return WriteSource(path); // nothing to lose, so do not ask

            // Without a hook there is nobody to ask, so keep the text.
            if (SaveFormatChooser is null) return WriteSource(path);

            var choice = SaveFormatChooser(path);
            if (choice is null) return "Save cancelled.";

            return choice switch
            {
                SaveFormat.Source => WriteSource(path),
                SaveFormat.Both => WriteSourceAndBytecode(path),
                _ => WriteBytecode(path),
            };
        }

        // Loaded from bytecode, so recompiling it is the whole point of the document.
        return WriteBytecode(path ?? string.Empty);
    }

    public override void SaveAs(string path)
    {
        Document.Text = Text;
        Document.SaveCompiled(path);
        Title = Document.Title;
        FinishSave();
    }

    private string WriteBytecode(string path)
    {
        Document.SaveCompiled(path);
        FinishSave();
        return $"Compiled to {Path.GetFileName(path)}." + WarningSuffix;
    }

    private string WriteSource(string path)
    {
        Document.SaveSource(path);
        FinishSave();
        return $"Saved source to {Path.GetFileName(path)}.";
    }

    /// <summary>
    /// Writes the source, then a compiled copy beside it. The bytecode is emitted without going
    /// through <see cref="LuaScriptDocument.SaveCompiled"/> so that the document stays bound to the
    /// source file rather than re-pointing itself at the .luac.
    /// </summary>
    private string WriteSourceAndBytecode(string path)
    {
        Document.SaveSource(path);

        string directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        string bytecodePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".luac");
        File.WriteAllBytes(bytecodePath, Document.Compile());

        FinishSave();
        return $"Saved {Path.GetFileName(path)} and compiled {Path.GetFileName(bytecodePath)}." + WarningSuffix;
    }

    /// <summary>Clears the tab's dirty marker and refreshes the lines that derive from the title.</summary>
    private void FinishSave()
    {
        IsDirty = false;
        Raise(nameof(Header), nameof(Detail), nameof(StatusText));
    }

    /// <summary>Re-runs the decompiler on the original bytecode, discarding edits.</summary>
    public void ReloadFromBytecode()
    {
        if (Document.OriginalBytecode is null) return;

        var output = LuaBytecodeService.Decompile(Document.OriginalBytecode, Settings);
        Document.Text = output.Source;
        Text = ApplyNamingIfEnabled(output.Source);
        IsDirty = false;
    }

    /// <summary>Writes the Lua source without compiling it.</summary>
    public void ExportSource(string path)
    {
        Document.Text = Text;
        Document.SaveSource(path);
    }

    /// <summary>
    /// Re-applies the AI argument names to freshly decompiled text. The view owns the preference, so
    /// it supplies this hook; without it the editor leaves the decompiler output untouched.
    /// </summary>
    public Func<string, string>? NamingApplier { get; set; }

    /// <summary>
    /// Asks the user what to write when a script that is only source text is saved, and returns null
    /// when they cancel. The view owns the dialog; without a hook the source is written back as-is.
    /// </summary>
    public Func<string, SaveFormat?>? SaveFormatChooser { get; set; }

    private string ApplyNamingIfEnabled(string source) =>
        NamingApplier is null ? source : NamingApplier(source);

    // ---- view state kept across tab switches ----------------------------------
    // The TabControl re-realises this editor's visual tree every time the tab is shown, so the
    // caret and the scroll position have to be remembered by the view model or they reset to the
    // top of the file on each switch.

    /// <summary>Where the caret was left, restored when this tab is shown again.</summary>
    public int CaretOffset { get; set; }
    public HashSet<string> CollapsedFunctions { get; } = new(StringComparer.Ordinal);

    /// <summary>Vertical scroll position, restored when this tab is shown again.</summary>
    public double ScrollOffsetY { get; set; }

    /// <summary>Horizontal scroll position, restored when this tab is shown again.</summary>
    public double ScrollOffsetX { get; set; }
}


/// <summary>An editable AI goal table.</summary>
public sealed class LuaInfoViewModel : DocumentViewModel
{
    private ObservableCollection<GoalRow> _goals = new();
    private string _filter = string.Empty;
    private ObservableCollection<LuaInfoIssue> _issues = new();

    public LuaInfoDocument Document { get; }

    public LuaInfoViewModel(LuaInfoDocument document)
    {
        Document = document;
        _goals = document.Goals;
        _goals.CollectionChanged += OnGoalsChanged;

        // The rows already in the document never arrive as a CollectionChanged notification, so
        // without this they would carry no edit handler. That is the normal case: a goal table
        // arrives from disk with every row in place before this constructor runs.
        foreach (var row in _goals) row.PropertyChanged += OnGoalChanged;

        View = CollectionViewSource.GetDefaultView(_goals);
        View.Filter = GoalMatchesFilter;

        Title = document.EntryName is not null ? Path.GetFileName(document.EntryName) : document.FilePath is null ? "New .luainfo" : Path.GetFileName(document.FilePath);
        Raise(nameof(GoalCount), nameof(LogicCount));
    }

    /// <summary>
    /// Keeps the tab marked dirty when a row is added or removed, and keeps the per-row edit
    /// handlers attached. Without the second half, typing into a grid cell would never set
    /// <see cref="IsDirty"/>, because a cell edit does not touch the collection.
    /// </summary>
    private void OnGoalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (GoalRow row in e.OldItems) row.PropertyChanged -= OnGoalChanged;
        if (e.NewItems is not null)
            foreach (GoalRow row in e.NewItems) row.PropertyChanged += OnGoalChanged;

        IsDirty = true;
        Raise(nameof(GoalCount), nameof(LogicCount), nameof(StatusText));
    }

    /// <summary>A cell edit in the grid, which changes the row but not the collection.</summary>
    private void OnGoalChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;
        Raise(nameof(LogicCount), nameof(StatusText));

        // A row can stop matching the filter because of its own edit, and the collection view has
        // no way of knowing that. Deferred, because refreshing during a DataGrid cell commit lands
        // in the middle of the edit the refresh itself caused.
        if (string.IsNullOrWhiteSpace(_filter)) return;
        if (e.PropertyName is not (nameof(GoalRow.Id) or nameof(GoalRow.Name) or nameof(GoalRow.LogicInterruptName)))
            return;

        Application.Current?.Dispatcher.BeginInvoke(
            () => View.Refresh(), DispatcherPriority.Background);
    }

    /// <summary>Filtered view over <see cref="Goals"/>; the grid binds to this.</summary>
    public ICollectionView View { get; }

    private bool GoalMatchesFilter(object item)
    {
        if (item is not GoalRow row) return false;
        if (string.IsNullOrWhiteSpace(_filter)) return true;

        return row.Id.ToString(CultureInfo.InvariantCulture).Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || (row.LogicInterruptName?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public ObservableCollection<GoalRow> Goals => _goals;

    public int GoalCount => Goals.Count;

    public int LogicCount => Document.LogicInterruptCount;

    /// <summary>Case-insensitive filter over ID, name and interrupt name.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;
            View.Refresh();
            Raise(nameof(FilterText));
        }
    }

    public string FilterText => string.IsNullOrWhiteSpace(Filter) ? "Showing all goals" : $"Filtered by '{Filter}'";

    public ObservableCollection<LuaInfoIssue> Issues
    {
        get => _issues;
        private set => Set(ref _issues, value);
    }

    public string IssueSummary
    {
        get
        {
            int errors = Issues.Count(i => i.Severity == LuaInfoIssueSeverity.Error);
            int warnings = Issues.Count(i => i.Severity == LuaInfoIssueSeverity.Warning);
            int infos = Issues.Count(i => i.Severity == LuaInfoIssueSeverity.Info);

            if (errors == 0 && warnings == 0 && infos == 0) return "No problems found";
            if (errors == 0 && warnings == 0) return $"Looks good ({infos} note{(infos == 1 ? "" : "s")})";

            var parts = new List<string>();
            if (errors > 0) parts.Add($"{errors} error{(errors == 1 ? "" : "s")}");
            if (warnings > 0) parts.Add($"{warnings} warning{(warnings == 1 ? "" : "s")}");
            if (infos > 0) parts.Add($"{infos} note{(infos == 1 ? "" : "s")}");
            return string.Join(", ", parts);
        }
    }

    public string IssueSummaryDetail =>
        "Info = normal FromSoftware layout.  Error = will misbehave in game.";

    public override string Detail =>
        $"{(Document.BigEndian ? "big endian" : "little endian")}, " +
        $"{(Document.LongFormat ? "64-bit offsets / UTF-16" : "32-bit offsets / Shift-JIS")}";

    public override string StatusText =>
        $"{Detail}   |   {GoalCount} goals   |   {LogicCount} with logic interrupt   |   {Document.FilePath ?? "unsaved"}";

    /// <summary>
    /// The row highlighted in the goal table. Notifies so a row chosen from a Lua editor chip also
    /// scrolls into view there.
    /// </summary>
    public GoalRow? SelectedRow
    {
        get => _selectedRow;
        set => Set(ref _selectedRow, value);
    }

    private GoalRow? _selectedRow;

    /// <summary>
    /// Every row the grid has selected, in the grid's own order. The grid is in multi-select mode,
    /// so delete and duplicate act on this set rather than on the single highlighted row. The view
    /// pushes it in from the DataGrid's selection, which WPF offers no way to bind directly.
    /// </summary>
    public ObservableCollection<GoalRow> SelectedRows { get; } = new();

    /// <summary>
    /// True when at least one row is selected, so the row commands have something to act on.
    /// The highlighted row counts too, so the buttons stay live even if the grid has not reported
    /// its selection yet.
    /// </summary>
    public bool HasGoalSelection => SelectedRows.Count > 0 || SelectedRow is not null;

    /// <summary>Mirrors the grid's selection. Called by the view whenever the selection changes.</summary>
    public void SetSelectedRows(IEnumerable<GoalRow> rows)
    {
        var selection = rows.Where(row => row is not null).ToList();
        if (selection.Count == SelectedRows.Count && SelectedRows.All(selection.Contains)) return;

        SelectedRows.Clear();
        foreach (var row in selection) SelectedRows.Add(row);
        OnPropertyChanged(nameof(HasGoalSelection));
    }

    /// <summary>
    /// Brings a row into view in the goal grid: clears the filter if it is hiding the row, then selects
    /// it so the grid scrolls to it.
    /// </summary>
    public void RevealRow(GoalRow row)
    {
        if (!string.IsNullOrEmpty(_filter) && !GoalMatchesFilter(row))
        {
            Filter = string.Empty;
            View.Refresh();
        }

        SelectedRow = row;
    }

    public void AddRow()
    {
        var row = Document.AddGoal();
        SelectedRow = row;
        IsDirty = true;
    }

    /// <summary>Adds the battle + logic pair that one AI group needs, sharing a single ID.</summary>
    public void AddGoalPair()
    {
        var (battle, logic) = Document.AddGoalPair();
        SelectedRow = battle;
        IsDirty = true;
        LastAddedPair = (battle, logic);
        Raise(nameof(LastAddedPairHint));
    }

    /// <summary>The pair added by the most recent <see cref="AddGoalPair"/>, so the view can select it.</summary>
    public (GoalRow Battle, GoalRow Logic)? LastAddedPair { get; private set; }

    public string LastAddedPairHint =>
        LastAddedPair is { } pair
            ? $"Added battle + logic goals on ID {pair.Battle.Id}"
            : string.Empty;

    public void RemoveRows(IEnumerable<GoalRow> rows)
    {
        var doomed = rows.Where(row => row is not null).ToList();
        foreach (var r in doomed) Document.RemoveGoal(r);

        // The removed rows are no longer valid selection targets.
        SetSelectedRows(SelectedRows.Except(doomed).ToList());
        if (SelectedRow is { } current && doomed.Contains(current)) SelectedRow = null;
        IsDirty = true;
    }

    public void DuplicateRow(GoalRow row)
    {
        Document.Duplicate(row);
        IsDirty = true;
    }

    public void MoveRow(GoalRow row, int delta)
    {
        Document.Move(row, delta);
        IsDirty = true;
    }

    public void SortById()
    {
        Document.SortById();
        IsDirty = true;
    }

    public void Validate()
    {
        Document.RefreshDirty();
        IsDirty = Document.IsDirty;
        Issues = new ObservableCollection<LuaInfoIssue>(Document.Validate());
        Raise(nameof(IssueSummary));
    }

    public override string Save()
    {
        if (Document.Archive is not null)
        {
            Document.SaveToArchive();
            Validate();
            return $"Saved {Document.EntryName} into {Path.GetFileName(Document.Archive.FilePath)}.";
        }
        if (string.IsNullOrEmpty(Document.FilePath))
            throw new InvalidOperationException("This goal table has never been saved; use Save As.");

        Document.Save(Document.FilePath);
        Validate();
        return $"Saved {Path.GetFileName(Document.FilePath)}.";
    }

    public override void SaveAs(string path)
    {
        Document.Save(path);
        Title = Path.GetFileName(path);
        Validate();
        Raise(nameof(Header), nameof(Detail), nameof(StatusText));
    }
}

/// <summary>A binder archive such as <c>aistub.luabnd</c>.</summary>
public sealed class ArchiveViewModel : DocumentViewModel
{
    private ArchiveEntry? _selectedEntry;

    public LuabndArchive Archive { get; }

    public ArchiveViewModel(LuabndArchive archive)
    {
        Archive = archive;
        Title = Path.GetFileName(archive.FilePath);
        Refresh();
    }

    public ObservableCollection<ArchiveEntry> Entries { get; private set; } = new();

    public ArchiveEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => Set(ref _selectedEntry, value);
    }

    public int EntryCount => Entries.Count;

    public int ScriptCount => Entries.Count(e => e.LooksLikeLua);

    public override string Detail =>
        $"{(Archive.IsBnd3 ? "BND3" : "BND4")}   |   {EntryCount} entries   |   {ScriptCount} Lua scripts";

    public override string StatusText => $"{Archive.FilePath}   |   {Detail}";

    public void Refresh()
    {
        Entries = new ObservableCollection<ArchiveEntry>(Archive.Entries);
        OnPropertyChanged(nameof(Entries));
        Raise(nameof(EntryCount), nameof(ScriptCount), nameof(Header));
    }

    public override string Save()
    {
        Archive.Save();
        IsDirty = false;
        return $"Wrote {Path.GetFileName(Archive.FilePath)}.";
    }

    public override void SaveAs(string path)
    {
        Archive.Save(path);
        Title = Path.GetFileName(path);
        Raise(nameof(Header), nameof(StatusText));
    }
}

/// <summary>The list of permitted global names.</summary>
public sealed class LuaGnlViewModel : DocumentViewModel
{
    public LuaGnlDocument Document { get; }

    public LuaGnlViewModel(LuaGnlDocument document)
    {
        Document = document;
        Title = document.EntryName is not null ? Path.GetFileName(document.EntryName) : document.FilePath is null ? "Globals" : Path.GetFileName(document.FilePath);
        Raise(nameof(GlobalsText), nameof(Count));
    }

    public string GlobalsText
    {
        get => string.Join(Environment.NewLine, Document.Globals);
        set
        {
            Document.Globals.Clear();
            Document.Globals.AddRange(value
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0));
            IsDirty = true;
            OnPropertyChanged(nameof(Count));
        }
    }

    public int Count => Document.Globals.Count;

    public override string Detail => $"{Count} global names | {(Document.LongFormat ? "64-bit / UTF-16" : "32-bit / Shift-JIS")}";

    public override string StatusText => $"{Detail}   |   {Document.FilePath ?? "unsaved"}";

    public override string Save()
    {
        if (Document.Archive is not null)
        {
            Document.SaveToArchive();
            IsDirty = false;
            return $"Saved {Document.EntryName} into {Path.GetFileName(Document.Archive.FilePath)}.";
        }
        if (string.IsNullOrEmpty(Document.FilePath))
            throw new InvalidOperationException("This globals list has never been saved; use Save As.");
        Document.Save(Document.FilePath);
        IsDirty = false;
        return $"Saved {Path.GetFileName(Document.FilePath)}.";
    }

    public override void SaveAs(string path)
    {
        Document.Save(path);
        Title = Path.GetFileName(path);
        IsDirty = false;
        Raise(nameof(Header), nameof(Detail), nameof(StatusText));
    }
}

