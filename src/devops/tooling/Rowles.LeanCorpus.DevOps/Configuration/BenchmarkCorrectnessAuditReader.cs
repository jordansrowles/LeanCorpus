using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal static class BenchmarkCorrectnessAuditReader
{
    private const int RequiredGateCount = 15;
    private const string FixtureRoot = ".config/devops/benchmark-correctness-fixtures/";

    public static BenchmarkCorrectnessAuditConfiguration Read(JsonElement root, string path, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        RejectDuplicateProperties(root, path, "$", caseInsensitive: true);
        RequireObjectProperties(root, path, "$",
            ["$schema", "schemaVersion", "specRevision", "auditedBranch", "auditedCommit", "auditedTreeDirty", "auditState", "gates"],
            ["$schema", "schemaVersion", "specRevision", "auditedBranch", "auditedCommit", "auditedTreeDirty", "auditState", "gates"]);

        if (ReadString(root.GetProperty("$schema"), path, "$.$schema") != "benchmark-correctness-audit.schema.json")
            throw Error(path, "$.$schema", "must identify benchmark-correctness-audit.schema.json");
        var schemaVersion = ReadInt(root.GetProperty("schemaVersion"), path, "$.schemaVersion");
        if (schemaVersion != 1)
            throw Error(path, "$.schemaVersion", "unsupported version; supported version is 1");
        var specRevision = ReadInt(root.GetProperty("specRevision"), path, "$.specRevision");
        if (specRevision != 3)
            throw Error(path, "$.specRevision", "must pin benchmark qualification specification revision 3");

        var auditedBranch = ReadString(root.GetProperty("auditedBranch"), path, "$.auditedBranch");
        var auditedCommit = ReadString(root.GetProperty("auditedCommit"), path, "$.auditedCommit");
        if (!IsGitCommit(auditedCommit))
            throw Error(path, "$.auditedCommit", "must be a full hexadecimal Git commit identity");
        var auditedTreeDirty = ReadBoolean(root.GetProperty("auditedTreeDirty"), path, "$.auditedTreeDirty");
        var auditState = ReadString(root.GetProperty("auditState"), path, "$.auditState");
        if (auditState != "PreRemediation")
            throw Error(path, "$.auditState", "must be PreRemediation for the initial defect audit");

        var gatesElement = root.GetProperty("gates");
        if (gatesElement.ValueKind != JsonValueKind.Array || gatesElement.GetArrayLength() != RequiredGateCount)
            throw Error(path, "$.gates", $"must contain exactly {RequiredGateCount} BVAL gates");

        var gates = ImmutableArray.CreateBuilder<BenchmarkCorrectnessAuditEntryConfiguration>(RequiredGateCount);
        var fixtureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in gatesElement.EnumerateArray())
        {
            var propertyPath = $"$.gates[{index}]";
            var gate = ReadGate(item, path, propertyPath, repositoryRoot, index + 1);
            if (!fixtureIds.Add(gate.FixtureId))
                throw Error(path, propertyPath + ".fixtureId", $"duplicates fixture ID '{gate.FixtureId}'");
            gates.Add(gate);
            index++;
        }

        return new BenchmarkCorrectnessAuditConfiguration(schemaVersion, specRevision, auditedBranch, auditedCommit,
            auditedTreeDirty, auditState, gates.ToImmutable());
    }

    private static BenchmarkCorrectnessAuditEntryConfiguration ReadGate(
        JsonElement value,
        string path,
        string propertyPath,
        string repositoryRoot,
        int ordinal)
    {
        RequireObjectProperties(value, path, propertyPath,
            ["id", "priority", "status", "claimClass", "competitiveClaimAllowed", "sourceFile", "symbol", "defect", "requiredProof", "fixtureId", "fixturePath", "fixtureSha256", "fixtureState"],
            ["id", "priority", "status", "claimClass", "competitiveClaimAllowed", "sourceFile", "symbol", "defect", "requiredProof", "fixtureId", "fixturePath", "fixtureSha256", "fixtureState"]);

        var expectedId = $"BVAL-{ordinal:D2}";
        var id = ReadString(value.GetProperty("id"), path, propertyPath + ".id");
        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            throw Error(path, propertyPath + ".id", $"expected '{expectedId}' in specification order");

        var priority = ReadString(value.GetProperty("priority"), path, propertyPath + ".priority");
        if (priority is not ("P0" or "P1"))
            throw Error(path, propertyPath + ".priority", "must be P0 or P1");
        var status = ReadString(value.GetProperty("status"), path, propertyPath + ".status");
        if (status is not ("Failing" or "Quarantined"))
            throw Error(path, propertyPath + ".status", "must be Failing or Quarantined before remediation");
        var claimClass = ReadString(value.GetProperty("claimClass"), path, propertyPath + ".claimClass");
        if (claimClass is not ("Directional" or "Unqualified"))
            throw Error(path, propertyPath + ".claimClass", "must be Directional or Unqualified before remediation");
        var competitiveClaimAllowed = ReadBoolean(value.GetProperty("competitiveClaimAllowed"), path, propertyPath + ".competitiveClaimAllowed");
        if (competitiveClaimAllowed)
            throw Error(path, propertyPath + ".competitiveClaimAllowed", "must remain false until the correctness gate is remediated and qualified");

        var sourceFile = ReadRepositoryFile(value.GetProperty("sourceFile"), path, propertyPath + ".sourceFile",
            repositoryRoot, "src/devops/benchmarks/Rowles.LeanCorpus.Benchmarks/");
        var symbol = ReadString(value.GetProperty("symbol"), path, propertyPath + ".symbol");
        var defect = ReadString(value.GetProperty("defect"), path, propertyPath + ".defect");
        var requiredProof = ReadString(value.GetProperty("requiredProof"), path, propertyPath + ".requiredProof");
        var fixtureId = ReadString(value.GetProperty("fixtureId"), path, propertyPath + ".fixtureId");
        if (!fixtureId.StartsWith($"bval-{ordinal:D2}-", StringComparison.Ordinal))
            throw Error(path, propertyPath + ".fixtureId", $"must identify fixture BVAL-{ordinal:D2}");
        var fixturePath = ReadString(value.GetProperty("fixturePath"), path, propertyPath + ".fixturePath");
        if (!fixturePath.StartsWith(FixtureRoot, StringComparison.Ordinal) ||
            !fixturePath.EndsWith(".json", StringComparison.Ordinal) ||
            fixturePath.Contains('\\') ||
            fixturePath.Split('/').Any(static part => part is "" or "." or ".."))
            throw Error(path, propertyPath + ".fixturePath", "must be a normalised repository-relative fixture path under benchmark-correctness-fixtures");
        var fixtureSha256 = ReadString(value.GetProperty("fixtureSha256"), path, propertyPath + ".fixtureSha256");
        if (!IsSha256(fixtureSha256))
            throw Error(path, propertyPath + ".fixtureSha256", "must be a lowercase SHA-256 digest");
        var fixtureState = ReadString(value.GetProperty("fixtureState"), path, propertyPath + ".fixtureState");
        if (fixtureState is not ("Defined" or "ExecutedFailing" or "ExecutedPassing"))
            throw Error(path, propertyPath + ".fixtureState", "must be Defined, ExecutedFailing or ExecutedPassing");

        VerifyFixture(path, propertyPath, repositoryRoot, fixturePath, fixtureId, fixtureSha256);
        return new BenchmarkCorrectnessAuditEntryConfiguration(id, priority, status, claimClass,
            competitiveClaimAllowed, sourceFile, symbol, defect, requiredProof, fixtureId, fixturePath,
            fixtureSha256, fixtureState);
    }

    private static void VerifyFixture(string auditPath, string propertyPath, string repositoryRoot,
        string fixturePath, string fixtureId, string expectedHash)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, fixturePath.Replace('/', Path.DirectorySeparatorChar)));
        var relativePath = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw Error(auditPath, propertyPath + ".fixturePath", "resolves outside the repository root");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Error(auditPath, propertyPath + ".fixturePath", $"could not read fixture '{fixturePath}': {exception.Message}");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            throw Error(auditPath, propertyPath + ".fixtureSha256", $"fixture SHA-256 does not match '{fixturePath}'");

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var fixture = document.RootElement;
            RejectDuplicateProperties(fixture, fixturePath, "$", caseInsensitive: true);
            RequireObjectProperties(fixture, fixturePath, "$",
                ["schemaVersion", "fixtureId", "deterministic", "seed", "domain", "input", "expected"],
                ["schemaVersion", "fixtureId", "deterministic", "seed", "domain", "input", "expected"]);
            if (ReadInt(fixture.GetProperty("schemaVersion"), fixturePath, "$.schemaVersion") != 1)
                throw Error(fixturePath, "$.schemaVersion", "unsupported version; supported version is 1");
            if (!string.Equals(ReadString(fixture.GetProperty("fixtureId"), fixturePath, "$.fixtureId"), fixtureId, StringComparison.Ordinal))
                throw Error(fixturePath, "$.fixtureId", $"does not match audit identity '{fixtureId}'");
            if (!ReadBoolean(fixture.GetProperty("deterministic"), fixturePath, "$.deterministic"))
                throw Error(fixturePath, "$.deterministic", "must be true");
            if (ReadInt(fixture.GetProperty("seed"), fixturePath, "$.seed") < 0)
                throw Error(fixturePath, "$.seed", "must be non-negative");
            _ = ReadString(fixture.GetProperty("domain"), fixturePath, "$.domain");
            RequireNonEmptyObject(fixture.GetProperty("input"), fixturePath, "$.input");
            RequireNonEmptyObject(fixture.GetProperty("expected"), fixturePath, "$.expected");
        }
        catch (JsonException exception)
        {
            throw Error(auditPath, propertyPath + ".fixturePath", $"fixture '{fixturePath}' is invalid JSON: {exception.Message}");
        }
    }

    private static string ReadRepositoryFile(JsonElement value, string path, string propertyPath,
        string repositoryRoot, string requiredPrefix)
    {
        var sourceFile = ReadString(value, path, propertyPath);
        if (!sourceFile.StartsWith(requiredPrefix, StringComparison.Ordinal) ||
            sourceFile.Contains('\\') ||
            sourceFile.Split('/').Any(static part => part is "" or "." or ".."))
            throw Error(path, propertyPath, $"must be a normalised repository-relative path under '{requiredPrefix}'");

        var root = Path.GetFullPath(repositoryRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, sourceFile.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(fullPath))
            throw Error(path, propertyPath, $"references missing source file '{sourceFile}'");
        return sourceFile;
    }

    private static void RequireNonEmptyObject(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Any())
            throw Error(path, propertyPath, "must be a non-empty object");
    }

    private static void RequireObjectProperties(JsonElement value, string path, string propertyPath,
        IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Error(path, propertyPath, "must be an object");
        var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name))
                throw Error(path, propertyPath, $"contains unknown property '{property.Name}'");
        }
        foreach (var name in required)
        {
            if (!value.TryGetProperty(name, out _))
                throw Error(path, propertyPath, $"is missing required property '{name}'");
        }
    }

    private static void RejectDuplicateProperties(JsonElement value, string path, string propertyPath, bool caseInsensitive)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw Error(path, propertyPath, $"contains duplicate property '{property.Name}'");
                RejectDuplicateProperties(property.Value, path, propertyPath + "." + property.Name, caseInsensitive);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item, path, $"{propertyPath}[{index}]", caseInsensitive);
                index++;
            }
        }
    }

    private static string ReadString(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Error(path, propertyPath, "must be a non-empty string");
        return value.GetString()!;
    }

    private static int ReadInt(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw Error(path, propertyPath, "must be an integer");
        return result;
    }

    private static bool ReadBoolean(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Error(path, propertyPath, "must be a boolean");
        return value.GetBoolean();
    }

    private static bool IsGitCommit(string value)
        => value.Length is 40 or 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException Error(string path, string propertyPath, string message)
        => new($"{Path.GetFileName(path)} {propertyPath}: {message}.");
}
