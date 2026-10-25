using System.Text.Json;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Shared.Diagnostics;

internal static class LeanCorpusTestDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void AttachJson<T>(string name, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TestContext.Current.AddAttachment(name, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "application/json");
    }

    public static void AttachIndexState<T>(T state) => AttachJson("index-state.json", state);

    public static void Warning(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        TestContext.Current.AddWarning(message);
    }

    public static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
}
