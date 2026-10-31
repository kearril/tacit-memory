using System.Text;
using ConsoleAppFramework;
using TacitMemory.Cli;

Console.OutputEncoding = Encoding.UTF8;

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args);
