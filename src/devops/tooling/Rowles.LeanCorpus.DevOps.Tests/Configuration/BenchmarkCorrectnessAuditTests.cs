using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Configuration;

public sealed class BenchmarkCorrectnessAuditTests
{
    [Fact]
    public void CheckedInAuditQuarantinesAllBvalGatesWithDeterministicFingerprintFixtures()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var auditPath = Path.Combine(checkout.RepositoryRoot, ".config", "devops", "benchmark-correctness-audit.json");

        Assert.True(File.Exists(auditPath), "The BVAL correctness audit must be checked in.");
        using var audit = JsonDocument.Parse(File.ReadAllBytes(auditPath));
        var gates = audit.RootElement.GetProperty("gates").EnumerateArray().ToArray();
        Assert.Equal(15, gates.Length);
        Assert.Equal(Enumerable.Range(1, 15).Select(static value => $"BVAL-{value:D2}"),
            gates.Select(static gate => gate.GetProperty("id").GetString()));

        var repositoryRoot = Path.GetFullPath(checkout.RepositoryRoot);
        foreach (var gate in gates)
        {
            var status = gate.GetProperty("status").GetString();
            Assert.True(status is "Failing" or "Quarantined", $"Unexpected gate status '{status}'.");
            Assert.False(gate.GetProperty("competitiveClaimAllowed").GetBoolean());

            var fixturePath = gate.GetProperty("fixturePath").GetString()!;
            Assert.StartsWith(".config/devops/benchmark-correctness-fixtures/", fixturePath, StringComparison.Ordinal);
            var fullFixturePath = Path.GetFullPath(Path.Combine(repositoryRoot, fixturePath));
            Assert.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, fullFixturePath, StringComparison.Ordinal);
            Assert.True(File.Exists(fullFixturePath), $"Fixture '{fixturePath}' is missing.");

            var fixtureBytes = File.ReadAllBytes(fullFixturePath);
            var fixtureHash = Convert.ToHexString(SHA256.HashData(fixtureBytes)).ToLowerInvariant();
            Assert.Equal(gate.GetProperty("fixtureSha256").GetString(), fixtureHash);

            using var fixture = JsonDocument.Parse(fixtureBytes);
            Assert.Equal(gate.GetProperty("fixtureId").GetString(), fixture.RootElement.GetProperty("fixtureId").GetString());
            Assert.True(fixture.RootElement.GetProperty("deterministic").GetBoolean());
            Assert.Equal(JsonValueKind.Object, fixture.RootElement.GetProperty("input").ValueKind);
            Assert.Equal(JsonValueKind.Object, fixture.RootElement.GetProperty("expected").ValueKind);
            Assert.NotEmpty(fixture.RootElement.GetProperty("input").EnumerateObject());
            Assert.NotEmpty(fixture.RootElement.GetProperty("expected").EnumerateObject());
        }
    }
}
