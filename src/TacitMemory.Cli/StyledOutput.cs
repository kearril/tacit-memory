using System.Runtime.ExceptionServices;
using Spectre.Console;
using Spectre.Console.Rendering;
using TacitMemory.Core;

namespace TacitMemory.Cli;

/// <summary>
/// The styled layout of the four outputs that have one: the status columns, the restore preview, the endpoint block of
/// <c>connect</c>, and the wait indicator. The commands use it only where the stream is a terminal and NO_COLOR is not
/// set (<see cref="CliConsole.StylesOut"/>, <see cref="CliConsole.StylesError"/>); everywhere else they print the plain
/// text. It changes layout and colour only: every word it shows is a word of the plain text. Which line is which, and how
/// each stands, comes from the report's structured data, never from its wording.
/// </summary>
internal static class StyledOutput
{
    private static readonly Style Bold = new(decoration: Decoration.Bold);
    private static readonly Style Highlight = new(Color.Yellow, decoration: Decoration.Bold);

    /// <summary>One line of a status column and the colour of its level; <c>null</c> leaves it uncoloured.</summary>
    private readonly record struct Line(string Text, Color? Colour);

    /// <summary>Prints each status column in a panel titled with the column's title, each line coloured by its level.</summary>
    public static void Status(TextWriter writer, StatusReport report)
    {
        var console = Create(writer);
        foreach (var column in report.Columns)
        {
            var rows = LinesOf(column).Select(line => (IRenderable)new Text(line.Text, StyleOf(line.Colour))).ToList();
            console.Write(new Panel(new Rows(rows)).Header(Markup.Escape(column.Title)).Expand());
        }
    }

    /// <summary>Prints the restore preview in a panel, with the lines that name what the restore replaces highlighted.</summary>
    public static void Preview(TextWriter writer, RestorePreview preview)
    {
        var rows = preview.ReportLines
            .Select(line => (IRenderable)new Text(line.Text, line.Replaces ? Highlight : Style.Plain))
            .ToList();
        Create(writer).Write(new Panel(new Rows(rows)).Expand());
    }

    /// <summary>
    /// Prints the report of <c>connect</c> with its endpoint block in a panel. The lines before and after the block are
    /// printed as they are.
    /// </summary>
    public static void Endpoint(TextWriter writer, BindingResult result)
    {
        foreach (var line in result.Before)
            writer.WriteLine(line);

        var rows = result.EndpointBlock.Select(line => (IRenderable)new Text(line, Bold)).ToList();
        Create(writer).Write(new Panel(new Rows(rows)).Expand());

        foreach (var line in result.After)
            writer.WriteLine(line);
    }

    /// <summary>
    /// Prints the probe report with each line coloured by its level: ok green, problem red, and a line with no level plain.
    /// Only the layout and colour change; the words are those of the plain report.
    /// </summary>
    public static void Probe(TextWriter writer, IReadOnlyList<ProbeReportLine> lines)
    {
        var rows = lines.Select(line => (IRenderable)new Text(line.Text, StyleOf(ColourOf(line.Level)))).ToList();
        Create(writer).Write(new Rows(rows));
    }

    /// <summary>
    /// Runs the call while a spinner and the elapsed time show on the error stream. Both are cleared when the call ends,
    /// and an exception from the call propagates unchanged. No percentage is shown: the service reports no progress.
    /// </summary>
    public static async Task<T> WithIndicatorAsync<T>(TextWriter writer, Func<Task<T>> call)
    {
        var console = Create(writer, interactive: true);
        // A terminal that reports no size, such as TERM=dumb on Linux and macOS, reads as height 0, and the live display
        // then fails while it crops to that height. Spectre's own fallback of 24 rows is used instead.
        if (console.Profile.Height < 1)
            console.Profile.Height = 24;
        T? result = default;
        Exception? failure = null;
        await console.Progress()
            .AutoClear(true)
            .Columns(new SpinnerColumn(), new ElapsedTimeColumn())
            .StartAsync(async context =>
            {
                // Spectre requires a description; the columns shown are the spinner and the elapsed time, so it is never drawn.
                var task = context.AddTask("call");
                try
                {
                    result = await call();
                }
                catch (Exception e)
                {
                    failure = e;
                }
                finally
                {
                    task.StopTask();
                }
            });

        // The progress block may wrap what it sees; the caller's handlers match on the call's own exception.
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    /// <summary>The lines of a status column, each with the colour of the level the report gives it.</summary>
    private static IEnumerable<Line> LinesOf(IStatusColumn column)
    {
        var levels = column.LineLevels();
        return column.Lines.Select((text, index) => new Line(text, ColourOf(levels[index])));
    }

    /// <summary>The colour of a status level: ok green, attention yellow, problem red; no level leaves the line plain.</summary>
    private static Color? ColourOf(StatusLevel? level) => level switch
    {
        StatusLevel.Ok => Color.Green,
        StatusLevel.Attention => Color.Yellow,
        StatusLevel.Problem => Color.Red,
        _ => null,
    };

    private static Style StyleOf(Color? colour) => colour is { } value ? new Style(value) : Style.Plain;

    /// <summary>
    /// A console that writes to <paramref name="writer"/>, whatever the process's own console is. Spectre's default
    /// enrichers are off: they turn interaction off when they see a CI variable such as <c>GITHUB_ACTIONS</c>, overriding
    /// the terminal checks this class already made.
    /// </summary>
    private static IAnsiConsole Create(TextWriter writer, bool interactive = false) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = ColorSystemSupport.Standard,
        Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
        Out = new AnsiConsoleOutput(writer),
        Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
    });
}
