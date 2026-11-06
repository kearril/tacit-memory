using System.Runtime.ExceptionServices;
using Spectre.Console;
using Spectre.Console.Rendering;
using TacitMemory.Core;

namespace TacitMemory.Cli;

/// <summary>
/// The styled layout of the four outputs that have one: the status columns, the restore preview, the endpoint block of
/// <c>connect</c>, and the wait indicator. The commands use it only where the stream is a terminal and NO_COLOR is not
/// set (<see cref="CliConsole.StylesOut"/>, <see cref="CliConsole.StylesError"/>); everywhere else they print the plain
/// text. It changes layout and colour only: every word it shows is a word of the plain text.
/// </summary>
internal static class StyledOutput
{
    /// <summary>The lines of the endpoint block: the endpoint, the transport and the server name.</summary>
    private const int EndpointLines = 3;

    private static readonly Style Bold = new(decoration: Decoration.Bold);
    private static readonly Style Highlight = new(Color.Yellow, decoration: Decoration.Bold);

    /// <summary>One line of a status column and the colour its structured field gives it; <c>null</c> leaves it uncoloured.</summary>
    private readonly record struct Line(string Text, Color? Colour);

    /// <summary>Prints each status column in a panel titled with the column's title, each line coloured by the field that says its state.</summary>
    public static void Status(TextWriter writer, StatusReport report)
    {
        var console = Create(writer);
        foreach (var column in new IStatusColumn?[]
                 {
                     report.Config, report.Service, report.Models, report.Bindings, report.Checkup,
                     report.Pending, report.Backups, report.Logs, report.Secrets,
                 })
        {
            if (column is null)
                continue;

            var rows = LinesOf(column).Select(line => (IRenderable)new Text(line.Text, StyleOf(line.Colour))).ToList();
            console.Write(new Panel(new Rows(rows)).Header(Markup.Escape(column.Title)).Expand());
        }
    }

    /// <summary>Prints the restore preview in a panel, with the lines that name what the restore replaces highlighted.</summary>
    public static void Preview(TextWriter writer, RestorePreview preview)
    {
        var replaces = new HashSet<string>(preview.Replaces, StringComparer.Ordinal);
        var rows = Lines(preview.Report)
            .Select(line => (IRenderable)new Text(line, replaces.Contains(line) ? Highlight : Style.Plain))
            .ToList();
        Create(writer).Write(new Panel(new Rows(rows)).Expand());
    }

    /// <summary>
    /// Prints the report of <c>connect</c> with its endpoint block in a panel. The block starts at the line that holds the
    /// endpoint, which the report prints first of its three lines; every other line is printed as it is.
    /// </summary>
    public static void Endpoint(TextWriter writer, BindingResult result)
    {
        var lines = Lines(result.Report);
        var start = result.Endpoint is null
            ? -1
            : Array.FindIndex(lines, line => line.Contains(result.Endpoint.ToString(), StringComparison.Ordinal));
        if (start < 0)
        {
            writer.WriteLine(result.Report);
            return;
        }

        var end = Math.Min(start + EndpointLines, lines.Length);
        foreach (var line in lines[..start])
            writer.WriteLine(line);

        var rows = lines[start..end].Select(line => (IRenderable)new Text(line, Bold)).ToList();
        Create(writer).Write(new Panel(new Rows(rows)).Expand());

        foreach (var line in lines[end..])
            writer.WriteLine(line);
    }

    /// <summary>
    /// Runs the call while a spinner and the elapsed time show on the error stream. Both are cleared when the call ends,
    /// and an exception from the call propagates unchanged. No percentage is shown: the service reports no progress.
    /// </summary>
    public static async Task<T> WithIndicatorAsync<T>(TextWriter writer, Func<Task<T>> call)
    {
        var console = Create(writer, interactive: true);
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

    /// <summary>The lines of a status column, each with the colour its structured field gives it.</summary>
    private static IEnumerable<Line> LinesOf(IStatusColumn column) => column switch
    {
        ConfigStatus config => Coloured(config.Lines, _ => Color.Red),
        ServiceStatus service => Coloured(service.Lines, _ => service.Running ? Color.Green : Color.Yellow),
        ModelsStatus models => Coloured(models.Lines, index => models.Items.ElementAtOrDefault(index) is { } model
            ? model.State == ModelState.Available ? Color.Green : Color.Yellow
            : null),
        BindingsStatus bindings => Coloured(bindings.Lines, index => bindings.Items.ElementAtOrDefault(index) is { } check
            ? check.Problem is null ? Color.Green : Color.Red
            : null),
        PendingStatus pending => Coloured(pending.Lines, index => index switch
        {
            0 => CountColour(pending.Confirmable),
            1 => CountColour(pending.FormatProblems),
            2 => pending.MainlineStale is null ? Color.Red : pending.MainlineStale.Value ? Color.Yellow : Color.Green,
            _ => null,
        }),
        BackupsStatus backups => Coloured(backups.Lines, index => index == 1 && backups.LastRemote is { } last
            ? last.Succeeded ? Color.Green : Color.Red
            : null),
        _ => Coloured(column.Lines, _ => null),
    };

    /// <summary>A count of items waiting: none is normal, some is pending, and an unreadable count is an error.</summary>
    private static Color? CountColour(int? count) => count is null ? Color.Red : count > 0 ? Color.Yellow : Color.Green;

    private static IEnumerable<Line> Coloured(IReadOnlyList<string> lines, Func<int, Color?> colourOf) =>
        lines.Select((text, index) => new Line(text, colourOf(index)));

    private static Style StyleOf(Color? colour) => colour is { } value ? new Style(value) : Style.Plain;

    /// <summary>The lines of a report, with the carriage returns of Windows line endings removed.</summary>
    private static string[] Lines(string report) => report.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    /// <summary>A console that writes to <paramref name="writer"/>, whatever the process's own console is.</summary>
    private static IAnsiConsole Create(TextWriter writer, bool interactive = false) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = ColorSystemSupport.Standard,
        Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
        Out = new AnsiConsoleOutput(writer),
    });
}
