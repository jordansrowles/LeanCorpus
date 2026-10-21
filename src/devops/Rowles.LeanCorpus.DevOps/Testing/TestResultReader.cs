using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal enum TestCaseOutcome
{
    Passed,
    Failed,
    Error,
    Timeout,
    Skipped,
    NotExecuted,
    NotRunnable,
    Aborted,
    Inconclusive
}

internal sealed record TestCaseResult(
    string StableId,
    string TestName,
    TestCaseOutcome Outcome,
    string? SkipReason,
    string? ErrorMessage,
    TimeSpan Duration,
    string? ReportId = null,
    string? ClassName = null,
    string? MethodName = null,
    string? StackTrace = null);

internal sealed record TestResultData(
    bool Exists,
    bool Valid,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TestCaseResult> Tests,
    string? Error);

internal static class TestResultReader
{
    private const long MaximumDocumentCharacters = 128 * 1024 * 1024;

    public static async Task<TestResultData> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return Invalid(exists: false, $"TRX result file '{path}' is missing.");
        }

        try
        {
            if (new FileInfo(path).Length > MaximumDocumentCharacters)
            {
                return Invalid(exists: true, $"TRX result file exceeds the {MaximumDocumentCharacters} character limit.");
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumDocumentCharacters,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            var summary = document.Descendants().FirstOrDefault(static element => element.Name.LocalName == "ResultSummary");
            var counters = summary?.Elements().FirstOrDefault(static element => element.Name.LocalName == "Counters");
            if (summary is null || counters is null)
            {
                return Invalid(exists: true, "TRX result file is missing its ResultSummary counters.");
            }

            var summaryOutcome = (string?)summary.Attribute("outcome");
            if (summaryOutcome is null || !new[] { "Completed", "Passed", "Failed" }
                    .Contains(summaryOutcome, StringComparer.OrdinalIgnoreCase))
            {
                return Invalid(exists: true, $"TRX result summary has non-terminal or missing outcome '{summaryOutcome}'.");
            }

            if (!TryReadCounter(counters, "total", out var total))
            {
                return Invalid(exists: true, "TRX result summary has a missing or invalid total counter.");
            }
            if (total < 1)
            {
                return Invalid(exists: true, "TRX result file reports zero test cases.");
            }

            var tests = new List<TestCaseResult>();
            var definitions = document.Descendants()
                .Where(static element => element.Name.LocalName == "UnitTest")
                .Select(static element => new
                {
                    Id = (string?)element.Attribute("id"),
                    Method = element.Elements().FirstOrDefault(static child => child.Name.LocalName == "TestMethod")
                })
                .Where(static definition => !string.IsNullOrWhiteSpace(definition.Id))
                .ToDictionary(static definition => definition.Id!, StringComparer.Ordinal);
            foreach (var result in document.Descendants().Where(static element => element.Name.LocalName == "UnitTestResult"))
            {
                var testId = (string?)result.Attribute("testId");
                var testName = (string?)result.Attribute("testName");
                var outcomeText = (string?)result.Attribute("outcome");
                if (string.IsNullOrWhiteSpace(testId) || string.IsNullOrWhiteSpace(testName) || string.IsNullOrWhiteSpace(outcomeText))
                {
                    return Invalid(exists: true, "TRX contains a test result without a stable test ID, name or outcome.");
                }
                if (!TryParseOutcome(outcomeText, out var outcome))
                {
                    return Invalid(exists: true, $"TRX contains unsupported test outcome '{outcomeText}'.");
                }

                var durationText = (string?)result.Attribute("duration");
                var duration = TimeSpan.TryParse(durationText, CultureInfo.InvariantCulture, out var parsedDuration)
                    ? parsedDuration
                    : TimeSpan.Zero;
                var message = result.Descendants().FirstOrDefault(static element => element.Name.LocalName == "Message")?.Value.Trim();
                var stackTrace = result.Descendants().FirstOrDefault(static element => element.Name.LocalName == "StackTrace")?.Value.Trim();
                var standardOutput = result.Descendants().FirstOrDefault(static element => element.Name.LocalName == "StdOut")?.Value.Trim();
                definitions.TryGetValue(testId, out var definition);
                var className = (string?)definition?.Method?.Attribute("className");
                var methodName = (string?)definition?.Method?.Attribute("name");
                var stableId = !string.IsNullOrWhiteSpace(className) && !string.IsNullOrWhiteSpace(methodName)
                    ? $"case:{className}|{methodName}|{testName}"
                    : $"id:{testId}";
                tests.Add(new TestCaseResult(
                    stableId,
                    testName,
                    outcome,
                    outcome is TestCaseOutcome.Skipped or TestCaseOutcome.NotRunnable or TestCaseOutcome.NotExecuted
                        ? FirstNonEmpty(message, standardOutput)
                        : null,
                    outcome is TestCaseOutcome.Failed or TestCaseOutcome.Error or TestCaseOutcome.Timeout ? message : null,
                    duration,
                    testId,
                    className,
                    methodName,
                    stackTrace));
            }

            if (tests.Count != total)
            {
                return Invalid(exists: true, $"TRX counter mismatch: summary reports {total} tests but contains {tests.Count} test results.");
            }

            var actualCounts = tests.GroupBy(static test => test.Outcome).ToDictionary(static group => group.Key, static group => group.Count());
            var expectedCounters = new (string Name, int Value)[]
            {
                ("passed", Count(TestCaseOutcome.Passed)),
                ("failed", Count(TestCaseOutcome.Failed)),
                ("error", Count(TestCaseOutcome.Error)),
                ("timeout", Count(TestCaseOutcome.Timeout)),
                ("inconclusive", Count(TestCaseOutcome.Inconclusive)),
                ("notRunnable", Count(TestCaseOutcome.NotRunnable)),
                ("notExecuted", Count(TestCaseOutcome.NotExecuted)),
                ("aborted", Count(TestCaseOutcome.Aborted)),
                ("executed", Count(TestCaseOutcome.Passed) + Count(TestCaseOutcome.Failed) + Count(TestCaseOutcome.Error) + Count(TestCaseOutcome.Timeout) + Count(TestCaseOutcome.Inconclusive))
            };
            foreach (var (name, expected) in expectedCounters)
            {
                var counter = counters.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (counter is null)
                {
                    continue;
                }
                if (!int.TryParse(counter.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var actual) || actual < 0)
                {
                    return Invalid(exists: true, $"TRX counter '{name}' is missing a valid non-negative integer value.");
                }
                if (actual != expected)
                {
                    return Invalid(exists: true, $"TRX counter mismatch: '{name}' reports {actual} but test results contain {expected}.");
                }
            }

            foreach (var name in new[] { "disconnected", "passedButRunAborted", "inProgress", "pending" })
            {
                var counter = counters.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (counter is null)
                {
                    continue;
                }
                if (!int.TryParse(counter.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value != 0)
                {
                    return Invalid(exists: true, $"TRX counter '{name}' indicates incomplete execution evidence.");
                }
            }

            var failed = Count(TestCaseOutcome.Failed) + Count(TestCaseOutcome.Error) + Count(TestCaseOutcome.Timeout);
            var skipped = Count(TestCaseOutcome.Skipped) + Count(TestCaseOutcome.NotRunnable);
            if (summaryOutcome.Equals("Passed", StringComparison.OrdinalIgnoreCase) && failed > 0 ||
                summaryOutcome.Equals("Failed", StringComparison.OrdinalIgnoreCase) && failed == 0)
            {
                return Invalid(exists: true, "TRX summary outcome does not agree with the individual test results.");
            }
            return new TestResultData(true, true, total, Count(TestCaseOutcome.Passed), failed, skipped, tests, null);

            int Count(TestCaseOutcome outcome)
                => actualCounts.TryGetValue(outcome, out var count) ? count : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException or ArgumentException)
        {
            return Invalid(exists: true, $"TRX result file is invalid: {exception.Message}");
        }
    }

    private static bool TryReadCounter(XElement counters, string name, out int value)
    {
        var text = counters.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static bool TryParseOutcome(string value, out TestCaseOutcome outcome)
    {
        switch (value.ToLowerInvariant())
        {
            case "passed": outcome = TestCaseOutcome.Passed; return true;
            case "failed": outcome = TestCaseOutcome.Failed; return true;
            case "error": outcome = TestCaseOutcome.Error; return true;
            case "timeout": outcome = TestCaseOutcome.Timeout; return true;
            case "skipped": outcome = TestCaseOutcome.Skipped; return true;
            case "notexecuted": outcome = TestCaseOutcome.NotExecuted; return true;
            case "notrunnable": outcome = TestCaseOutcome.NotRunnable; return true;
            case "aborted": outcome = TestCaseOutcome.Aborted; return true;
            case "inconclusive": outcome = TestCaseOutcome.Inconclusive; return true;
            default: outcome = default; return false;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static TestResultData Invalid(bool exists, string message)
        => new(exists, false, 0, 0, 0, 0, [], message);
}
