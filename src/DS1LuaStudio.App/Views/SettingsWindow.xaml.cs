using System.Windows;
using DS1LuaStudio.App.Services;

namespace DS1LuaStudio.App.Views;

public partial class SettingsWindow : Window
{
    private static readonly string[] Fonts =
    {
        "Consolas", "Cascadia Mono", "Cascadia Code", "Courier New", "Lucida Console", "Segoe UI",
    };

    private AppSettings _working;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();

        _working = current.Copy();

        FontFamilyBox.ItemsSource = Fonts;
        FontFamilyBox.SelectedItem = Fonts.FirstOrDefault(f =>
            _working.EditorFontFamily.StartsWith(f, StringComparison.OrdinalIgnoreCase)) ?? Fonts[0];

        Fill(EditorForegroundBox, ColorPresets.CodeText, _working.EditorForeground);
        Fill(EditorBackgroundBox, ColorPresets.CodeBackground, _working.EditorBackground);
        Fill(InterfaceTextBox, ColorPresets.InterfaceText, _working.InterfaceText);
        Fill(AccentBox, ColorPresets.Accent, _working.Accent);

        FontSizeSlider.Value = Math.Clamp(_working.EditorFontSize, 10, 22);
        UpdateFontSizeLabel();
        LineNumbersCheck.IsChecked = _working.ShowLineNumbers;
        Compile32BitCheck.IsChecked = _working.CompileLua50As32Bit;
        Compile32BitCheck.IsEnabled = _working.GameMode == DS1LuaStudio.Core.GameMode.DS1;
        NameArgsCheck.IsChecked = _working.NameAiArgumentsFromGoals;
        WordWrapCheck.IsChecked = _working.WordWrap;

        FontSizeSlider.ValueChanged += (_, _) => UpdateFontSizeLabel();
    }

    /// <summary>Live preview while the dialog is open.</summary>
    public AppSettings Current => _working with
    {
        EditorForeground = Pick(EditorForegroundBox, _working.EditorForeground),
        EditorBackground = Pick(EditorBackgroundBox, _working.EditorBackground),
        InterfaceText = Pick(InterfaceTextBox, _working.InterfaceText),
        Accent = Pick(AccentBox, _working.Accent),
        EditorFontFamily = FontFamilyBox.SelectedItem?.ToString() ?? Fonts[0],
        EditorFontSize = FontSizeSlider.Value,
        ShowLineNumbers = LineNumbersCheck.IsChecked == true,
        WordWrap = WordWrapCheck.IsChecked == true,
        CompileLua50As32Bit = Compile32BitCheck.IsChecked == true,
        NameAiArgumentsFromGoals = NameArgsCheck.IsChecked == true,
    };

    public AppSettings? Result { get; private set; }

    private static void Fill(System.Windows.Controls.ComboBox box, ColorPreset[] presets, string hex)
    {
        box.ItemsSource = presets;
        box.SelectedItem = presets.FirstOrDefault(p => p.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase))
                           ?? presets.FirstOrDefault(p => Near(p, hex));
    }

    /// <summary>Falls back to the closest preset so unknown hex values still show something sensible.</summary>
    private static bool Near(ColorPreset preset, string hex)
    {
        try
        {
            var a = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(preset.Hex)!;
            var b = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            int distance = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            return distance < 24;
        }
        catch
        {
            return false;
        }
    }

    private static string Pick(System.Windows.Controls.ComboBox box, string fallback) =>
        box.SelectedItem is ColorPreset p ? p.Hex : fallback;

    private void UpdateFontSizeLabel()
    {
        if (FontSizeLabel is not null)
            FontSizeLabel.Text = ((int)Math.Round(FontSizeSlider.Value)).ToString();
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        Result = Current;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        _working = defaults.Copy();

        Fill(EditorForegroundBox, ColorPresets.CodeText, defaults.EditorForeground);
        Fill(EditorBackgroundBox, ColorPresets.CodeBackground, defaults.EditorBackground);
        Fill(InterfaceTextBox, ColorPresets.InterfaceText, defaults.InterfaceText);
        Fill(AccentBox, ColorPresets.Accent, defaults.Accent);

        FontSizeSlider.Value = defaults.EditorFontSize;
        FontFamilyBox.SelectedItem = Fonts[0];
        LineNumbersCheck.IsChecked = defaults.ShowLineNumbers;
        WordWrapCheck.IsChecked = defaults.WordWrap;
        Compile32BitCheck.IsChecked = defaults.CompileLua50As32Bit;
        NameArgsCheck.IsChecked = defaults.NameAiArgumentsFromGoals;
    }
}
