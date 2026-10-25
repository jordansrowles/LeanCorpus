using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

if (args.Length == 0)
{
    Console.Error.WriteLine("A process fixture mode is required.");
    return 2;
}

switch (args[0])
{
    case "noisy":
    {
        var count = int.Parse(args[1]);
        var stdout = Task.Run(() => WriteLines(Console.Out, "out", count));
        var stderr = Task.Run(() => WriteLines(Console.Error, "err", count));
        await Task.WhenAll(stdout, stderr);
        if (args.Length > 2)
        {
            await Task.Delay(int.Parse(args[2]));
        }
        return 0;
    }
    case "output-handshake":
        Console.Out.WriteLine("out-ready");
        Console.Out.Flush();
        Console.Error.WriteLine("err-ready");
        Console.Error.Flush();
        while (!File.Exists(args[1]))
        {
            await Task.Delay(10);
        }
        return 0;
    case "long-line":
        Console.WriteLine(new string('x', int.Parse(args[1])));
        return 0;
    case "echo":
        Console.WriteLine(JsonSerializer.Serialize(args.Skip(1)));
        Console.WriteLine(Environment.CurrentDirectory);
        return 0;
    case "environment":
        Console.WriteLine(Environment.GetEnvironmentVariable(args[1]) ?? "<unset>");
        return 0;
    case "stdin":
        Console.Write(await Console.In.ReadToEndAsync());
        return 0;
    case "nul-output":
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\0', args.Skip(1)) + '\0');
        await Console.OpenStandardOutput().WriteAsync(bytes);
        return 0;
    }
    case "readiness":
        Console.WriteLine("      Now listening: http://127.0.0.1:5080");
        await Task.Delay(int.Parse(args[1]));
        return 0;
    case "exit":
        return int.Parse(args[1]);
    case "sleep":
    case "silent":
        await Task.Delay(int.Parse(args[1]));
        return 0;
    default:
        Console.Error.WriteLine($"Unknown process fixture mode '{args[0]}'.");
        return 2;
}

static void WriteLines(TextWriter writer, string prefix, int count)
{
    for (var i = 0; i < count; i++)
    {
        writer.WriteLine($"{prefix}-{i:D5}");
    }
}
