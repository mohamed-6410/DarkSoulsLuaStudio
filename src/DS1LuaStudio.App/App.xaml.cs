using System.Windows;
using System.Windows.Threading;
using DS1LuaStudio.App.Services;
using DS1LuaStudio.Core;

namespace DS1LuaStudio.App;

public partial class App : Application
{
    private bool _errorAlreadyShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        // DS1 strings are Shift-JIS; make sure the code pages provider is registered before any
        // dialog tries to read an AI file.
        LuaBytecodeService.EnsureEncodings();

        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // AvalonEdit 6 has null references in its layout code that fire while a TextEditor is being
        // swapped or torn down: its I-beam hit-test, and the construction of visual lines (once per
        // line, so a single tab switch can raise hundreds). They are re-raised on every subsequent
        // mouse move or layout pass and cost nothing but the cursor shape and some redraw work, so
        // dropping them beats interrupting the user. Logged once, collapsed by Log's repeat filter.
        if (IsBenignAvalonEditLayout(e.Exception))
        {
            Log.Warn("AvalonEdit layout null reference (ignored): " + e.Exception.Message +
                     Environment.NewLine + e.Exception.StackTrace);
            return;
        }

        Log.Error("Unhandled exception", e.Exception);

        // Repeated failures (for example a styling error re-raised per layout pass) would otherwise
        // stack up an unbounded number of dialogs, so only the first one is shown.
        if (_errorAlreadyShown) return;
        _errorAlreadyShown = true;

        Services.DialogService.Info(
            "Something went wrong:\n\n" + e.Exception.Message +
            "\n\nThe file you were editing has not been modified.\n\n" +
            "Details were written to:\n" + Log.LogPath);
    }

    /// <summary>
    /// AvalonEdit 6 null-references itself while a <c>TextEditor</c> is being swapped or torn down - in
    /// its I-beam hit-test, in visual-line construction, when options are re-applied, and in IME
    /// handling. Each fires once per visual line or per mouse move, so a single tab switch can raise
    /// hundreds and a log grows without bound.
    /// <para>
    /// The rule is deliberately narrow: the exception must be a null reference, its stack must contain
    /// an AvalonEdit frame, and it must contain <em>none</em> of ours. If our code is in the stack it is
    /// our bug and gets reported. Everything dropped here is still logged once at WARN, collapsed by
    /// <see cref="Log"/>'s repeat filter so the evidence survives without the volume.
    /// </para>
    /// </summary>
    private static bool IsBenignAvalonEditLayout(Exception ex) =>
        ex is NullReferenceException &&
        ex.StackTrace?.Contains("ICSharpCode.AvalonEdit") == true &&
        ex.StackTrace.Contains("DS1LuaStudio") == false;
}
