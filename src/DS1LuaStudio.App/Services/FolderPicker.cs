namespace DS1LuaStudio.App.Services;

/// <summary>
/// Folder chooser. WPF has no built-in folder dialog; this uses the WinForms one, which ships
/// with the framework, rather than pulling in a third party package.
/// </summary>
internal static class FolderPicker
{
    public static string? PickFolder(string title)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}

internal static class DialogService
{
    public static bool Confirm(string message) =>
        System.Windows.MessageBox.Show(
            message,
            "DS1 Lua Studio",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;

    public static void Info(string message) =>
        System.Windows.MessageBox.Show(
            message,
            "DS1 Lua Studio",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
}
