namespace Rowles.DataForge.Tool;

internal static class Program
{
    private static int Main(string[] args) =>
        DataForgeCommandLine.Run(args, Console.Out, Console.Error, Directory.GetCurrentDirectory());
}
