using System.Text;
using TacitMemory.Cli;
using TacitMemory.Core;

// The only place the process's streams and environment are read; everything else gets them through CommandLine.
Console.OutputEncoding = Encoding.UTF8;
var console = new CliConsole(
    Console.Out,
    Console.Error,
    Console.In,
    OutIsTerminal: !Console.IsOutputRedirected,
    ErrorIsTerminal: !Console.IsErrorRedirected,
    InIsTerminal: !Console.IsInputRedirected,
    NoColor: !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));
return await CommandLine.RunAsync(args, TacitOptions.FromEnvironment(), console);
