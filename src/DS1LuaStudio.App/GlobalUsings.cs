// WinForms is referenced only for the folder chooser, but it injects System.Drawing and
// System.Windows.Forms into the implicit usings. Pin the WPF types we actually mean so that
// Color / Brush / Application are never ambiguous.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Color = System.Windows.Media.Color;
global using ResourceDictionary = System.Windows.ResourceDictionary;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Binding = System.Windows.Data.Binding;
global using FontFamily = System.Windows.Media.FontFamily;
