using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

if (Environment.GetEnvironmentVariable("LEANCORPUS_BOOTSTRAP_TEST_STUB") == "1")
{
    return await RunBootstrapStubAsync(args);
}

if (args.Length == 0)
{
    await Console.Error.WriteLineAsync("A process fixture mode is required.");
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
        await Console.Out.WriteLineAsync("out-ready");
        await Console.Out.FlushAsync();
        await Console.Error.WriteLineAsync("err-ready");
        await Console.Error.FlushAsync();
        while (!File.Exists(args[1]))
        {
            await Task.Delay(10);
        }
        return 0;
    case "wait-for-release":
    {
        string processIdPath = args[1] + ".tmp";
        await File.WriteAllTextAsync(processIdPath, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.Move(processIdPath, args[1], overwrite: true);
        Console.Out.WriteLine("child-ready");
        Console.Out.Flush();
        while (!File.Exists(args[2]))
        {
            await Task.Delay(10);
        }
        return 0;
    }
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
        await Console.Out.WriteLineAsync("      Now listening: http://127.0.0.1:5080");
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

static async Task<int> RunBootstrapStubAsync(string[] arguments)
{
    var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
    if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
    {
        return await RunFakeDotNetAsync(arguments);
    }

    var failureStage = Environment.GetEnvironmentVariable("BOOTSTRAP_FAKE_FAIL_STAGE") ?? string.Empty;
    if (arguments.Length == 0)
    {
        return failureStage == "test" ? 35 : 0;
    }

    if (arguments[0] == "--help")
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        var staged = processPath.Contains("bootstrap-staging", StringComparison.OrdinalIgnoreCase);
        if ((failureStage == "staged-help" && staged) || (failureStage == "promoted-help" && !staged))
        {
            return failureStage == "staged-help" ? 17 : 18;
        }

        Console.WriteLine("LeanCorpus managed DevOps fixture");
        return 0;
    }

    if (arguments[0] == "--version")
    {
        Console.WriteLine("1.0.0-fixture");
        return 0;
    }

    var capturePath = Environment.GetEnvironmentVariable("DEVOPS_CAPTURE_PATH");
    if (!string.IsNullOrWhiteSpace(capturePath))
    {
        await File.WriteAllLinesAsync(capturePath, arguments);
    }

    return int.TryParse(Environment.GetEnvironmentVariable("DEVOPS_FAKE_DISPATCH_EXIT_CODE"), out var exitCode)
        ? exitCode
        : 0;
}

static Task<int> RunFakeDotNetAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return Task.FromResult(2);
    }

    var failureStage = Environment.GetEnvironmentVariable("BOOTSTRAP_FAKE_FAIL_STAGE") ?? string.Empty;
    switch (arguments[0])
    {
        case "--list-sdks":
            Console.WriteLine(failureStage == "missing-sdk"
                ? "10.0.400 [/fake/sdk]"
                : "11.0.100-preview.7.26381.103 [/fake/sdk]");
            return Task.FromResult(0);
        case "--version":
            Console.WriteLine("11.0.100-preview.7.26381.103");
            return Task.FromResult(0);
        case "tool" when failureStage == "tool-restore":
            return Task.FromResult(31);
        case "restore":
            CaptureBootstrapCommand("BOOTSTRAP_RESTORE_CAPTURE_PATH", arguments);
            return Task.FromResult(failureStage == "restore" ? 32 : 0);
        case "build":
            CaptureBootstrapCommand("BOOTSTRAP_BUILD_CAPTURE_PATH", arguments);
            if (failureStage == "build")
            {
                return Task.FromResult(33);
            }

            var testApplication = Path.Combine(
                Environment.CurrentDirectory,
                "artifacts", "devops", "bin", "Rowles.LeanCorpus.DevOps.Tests", "release",
                OperatingSystem.IsWindows() ? "Rowles.LeanCorpus.DevOps.Tests.exe" : "Rowles.LeanCorpus.DevOps.Tests");
            CopyBootstrapStub(testApplication);
            return Task.FromResult(0);
        case "publish":
            CaptureBootstrapCommand("BOOTSTRAP_PUBLISH_CAPTURE_PATH", arguments);
            if (failureStage == "publish")
            {
                return Task.FromResult(34);
            }

            var outputIndex = Array.IndexOf(arguments, "--output");
            if (outputIndex < 0 || outputIndex + 1 >= arguments.Length)
            {
                return Task.FromResult(2);
            }

            var outputDirectory = arguments[outputIndex + 1];
            Directory.CreateDirectory(outputDirectory);
            var hostPath = Path.Combine(outputDirectory, OperatingSystem.IsWindows() ? "leancorpus-devops.exe" : "leancorpus-devops");
            CopyBootstrapStub(hostPath);
            if (Environment.GetEnvironmentVariable("BOOTSTRAP_FAKE_SIDECAR") == "1")
            {
                File.WriteAllText(hostPath + ".deps.json", "unexpected sidecar");
            }

            return Task.FromResult(0);
        default:
            return Task.FromResult(0);
    }
}

static void CaptureBootstrapCommand(string environmentVariable, IReadOnlyList<string> arguments)
{
    var path = Environment.GetEnvironmentVariable(environmentVariable);
    if (!string.IsNullOrWhiteSpace(path))
    {
        File.AppendAllText(path, string.Join(' ', arguments) + Environment.NewLine);
    }
}

static void CopyBootstrapStub(string destinationPath)
{
    var templatePath = Environment.GetEnvironmentVariable("LEANCORPUS_BOOTSTRAP_STUB_TEMPLATE_PATH");
    if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
    {
        throw new FileNotFoundException("The bootstrap test stub template is unavailable.", templatePath);
    }

    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
    File.Copy(templatePath, destinationPath, overwrite: true);
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(destinationPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
