using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Media;

namespace DS1LuaStudio.App.Services;

public sealed class LuaCompletionData(string text, string? description, string kind = "AI", double priority = 0) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => text;
    public object Content
    {
        get
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, FlowDirection = System.Windows.FlowDirection.LeftToRight };
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = kind, Width = 66, Foreground = System.Windows.Application.Current?.TryFindResource("BrushAccent") as Brush, FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), MaxWidth = 380,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis, Foreground = System.Windows.Application.Current?.TryFindResource("BrushTextPrimary") as Brush });
            return row;
        }
    }
    public object Description => description ?? text;
    public double Priority => priority;
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, text);
}
