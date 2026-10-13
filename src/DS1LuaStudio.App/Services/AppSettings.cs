using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace DS1LuaStudio.App.Services;

/// <summary>A named colour the user can pick from in the appearance settings.</summary>
public sealed record ColorPreset(string Name, string Hex);

public static class ColorPresets
{
    // Code foreground. The first few are the "make it white or silver" options.
    public static readonly ColorPreset[] CodeText =
    {
        new("White", "#FFFFFFFF"),
        new("Silver", "#FFC9D1DC"),
        new("Light silver", "#FFAFB8C6"),
        new("Warm white", "#FFF3EEE6"),
        new("Pale blue", "#FFD8E6F7"),
        new("Soft green", "#FFDCEFDC"),
        new("Soft amber", "#FFF2E2C0"),
        new("Dim grey", "#FF8B93A1"),
        new("Black (light background)", "#FF101216"),
    };

    public static readonly ColorPreset[] CodeBackground =
    {
        new("Charcoal", "#FF12141A"),
        new("Almost black", "#FF0B0C10"),
        new("Dark slate", "#FF171B22"),
        new("Warm dark", "#FF171512"),
        new("Navy dark", "#FF10141C"),
        new("Mid grey", "#FF2A2F38"),
        new("Light paper", "#FFEDEDEA"),
        new("White", "#FFFFFFFF"),
    };

    public static readonly ColorPreset[] InterfaceText =
    {
        new("White", "#FFE6E9EF"),
        new("Silver", "#FFC9D1DC"),
        new("Soft grey", "#FFB3BAC6"),
        new("Warm white", "#FFF3EEE6"),
        new("Black", "#FF14161A"),
    };

    public static readonly ColorPreset[] Accent =
    {
        new("Gold", "#FFE0A340"),
        new("Amber", "#FFE8A33D"),
        new("Orange", "#FFE2762F"),
        new("Crimson", "#FFD4453C"),
        new("Rose", "#FFD8608F"),
        new("Purple", "#FF9B6BD8"),
        new("Blue", "#FF5B9BE8"),
        new("Teal", "#FF3FBFA8"),
        new("Green", "#FF6FCF7F"),
        new("Silver", "#FFC0C6D1"),
    };
}

/// <summary>User appearance preferences, persisted in the app data folder.</summary>
public sealed record AppSettings
{
    public DS1LuaStudio.Core.GameMode GameMode { get; set; }
    public bool EnableAutocomplete { get; set; } = true;
    /// <summary>Code text colour. Defaults to white; silver and other tones are one click away.</summary>
    public string EditorForeground { get; set; } = "#FFFFFFFF";

    public string EditorBackground { get; set; } = "#FF12141A";
    public string InterfaceText { get; set; } = "#FFE6E9EF";
    public string Accent { get; set; } = "#FFE0A340";

    public double EditorFontSize { get; set; } = 13;
    public string EditorFontFamily { get; set; } = "Consolas";
    public bool ShowLineNumbers { get; set; } = true;
    public bool WordWrap { get; set; }

    /// <summary>
    /// Emit Lua 5.0 bytecode with a 32-bit size_t so Dark Souls PTDE can load it. The bundled
    /// compiler natively emits 64-bit lengths; turn this off to keep the compiler's own layout.
    /// </summary>
    public bool CompileLua50As32Bit { get; set; } = true;

    /// <summary>
    /// Replace generated argument names (<c>f1_arg0</c>) with the real AI callback parameters
    /// (<c>ai</c>, <c>goal</c>, <c>paramTbl</c>) using the goal tables found near the script.
    /// </summary>
    public bool NameAiArgumentsFromGoals { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DS1LuaStudio",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A broken settings file must never stop the app from opening.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Settings are a convenience; failing to persist them is not worth interrupting work.
        }
    }

    public AppSettings Copy() => this with { };
}

/// <summary>
/// Pushes <see cref="AppSettings"/> into the live resource dictionary.
/// <para>
/// The brush objects themselves are replaced rather than only their colours. A
/// <c>SolidColorBrush.Color</c> set through a DynamicResource does not reliably re-resolve when
/// the referenced colour changes, so replacing the brush is what actually repaints the running UI.
/// Every style in the theme references these brushes via DynamicResource, which does update.
/// </para>
/// </summary>
public static class ThemeManager
{
    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var panel = Parse("#FF1D2026");

        var primary = Parse(settings.InterfaceText);
        var secondary = Mix(primary, panel, 0.42);
        var muted = Mix(primary, panel, 0.62);

        var accent = Parse(settings.Accent);

        Set(resources, "TextPrimary", primary);
        Set(resources, "TextSecondary", secondary);
        Set(resources, "TextMuted", muted);
        Set(resources, "Accent", accent);

        SetBrush(resources, "BrushTextPrimary", primary);
        SetBrush(resources, "BrushTextSecondary", secondary);
        SetBrush(resources, "BrushTextMuted", muted);
        SetBrush(resources, "BrushAccent", accent);
        SetBrush(resources, "BrushAccentText", ReadableTextOn(accent));
        SetBrush(resources, "BrushWarning", Mix(accent, panel, 0.15));

        var editorBg = Parse(settings.EditorBackground);
        var editorFg = Parse(settings.EditorForeground);

        Set(resources, "EditorBg", editorBg);
        Set(resources, "EditorFg", editorFg);

        SetBrush(resources, "EditorBackground", editorBg);
        SetBrush(resources, "EditorForeground", editorFg);
        SetBrush(resources, "EditorCaret", accent);
        SetBrush(resources, "EditorSelection", Lighten(editorBg, 0.18));
        SetBrush(resources, "EditorSelectionFg", editorFg);
        SetBrush(resources, "EditorLineNumbers", muted);
        SetBrush(resources, "EditorCurrentLine", Lighten(editorBg, 0.04));
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        try { resources[key] = color; } catch { /* palette key not present in this theme */ }
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        try { resources[key] = new SolidColorBrush(color); } catch { /* key not present */ }
    }

    private static Color Parse(string hex)
    {
        try
        {
            return ColorConverter.ConvertFromString(hex) is Color c ? c : Colors.White;
        }
        catch
        {
            return Colors.White;
        }
    }

    /// <summary>Blends <paramref name="color"/> towards <paramref name="towards"/>.</summary>
    private static Color Mix(Color color, Color towards, double amount) =>
        Color.FromArgb(color.A,
            Blend(color.R, towards.R, amount),
            Blend(color.G, towards.G, amount),
            Blend(color.B, towards.B, amount));

    /// <summary>Lightens a colour towards white.</summary>
    private static Color Lighten(Color color, double amount) => Mix(color, Colors.White, amount);

    private static byte Blend(byte from, byte to, double amount) =>
        (byte)Math.Clamp(from + (to - from) * amount, 0, 255);

    /// <summary>Black or white, whichever stays readable on the given background.</summary>
    private static Color ReadableTextOn(Color background)
    {
        // Rec. 709 relative luminance.
        double luminance = (0.2126 * Channel(background.R)
                          + 0.7152 * Channel(background.G)
                          + 0.0722 * Channel(background.B));

        return luminance > 0.55 ? Colors.Black : Colors.White;
    }

    private static double Channel(byte value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
