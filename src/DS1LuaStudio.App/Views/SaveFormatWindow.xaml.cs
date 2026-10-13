using System.IO;
using System.Windows;
using DS1LuaStudio.App.ViewModels;

namespace DS1LuaStudio.App.Views;

/// <summary>
/// Asked when saving a script that has no bytecode behind it, because the answer decides whether
/// the file on disk stays readable source or becomes a binary chunk. Compiling over hand written
/// source cannot be undone, so the question has to be asked rather than assumed.
/// </summary>
public partial class SaveFormatWindow : Window
{
    public SaveFormatWindow(string path)
    {
        InitializeComponent();

        string name = Path.GetFileName(path);

        IntroText.Text = $"'{name}' is a Lua source file, not bytecode. Dark Souls reads compiled " +
                         "chunks, but a compiled chunk cannot be edited or diffed.";

        bool overwriteSource = File.Exists(path);
        WarningBorder.Visibility = overwriteSource ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text =
            $"'{name}' already exists and holds source you may not have in the game's files. " +
            "Writing bytecode over it discards that text.";

        // Source is the safe default, so Enter cannot destroy the file by accident.
        SourceOption.IsChecked = true;
    }

    /// <summary>What the user chose, once the dialog has been accepted.</summary>
    public SaveFormat Result { get; private set; } = SaveFormat.Source;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Result = BothOption.IsChecked == true ? SaveFormat.Both
                : CompiledOption.IsChecked == true ? SaveFormat.Compiled
                : SaveFormat.Source;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}