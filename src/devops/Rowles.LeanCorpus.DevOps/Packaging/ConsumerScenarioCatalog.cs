namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record ConsumerScenario(
    string Name,
    string PackageId,
    IReadOnlyList<string> PackageIds,
    IReadOnlyList<string> ExactInternalIds,
    string Kind,
    string Framework,
    string Sdk,
    string ExtraProperties,
    IReadOnlyList<string> ExternalReferences,
    string RuntimeKind);

internal static class ConsumerScenarioCatalog
{
    private static readonly string[] Frameworks = ["net10.0", "net11.0"];

    public static IReadOnlyList<ConsumerScenario> Create(string scope)
    {
        if (scope is not ("all" or "compression"))
        {
            throw new ArgumentException("Package consumer scope must be 'all' or 'compression'.", nameof(scope));
        }

        var scenarios = new List<ConsumerScenario>();
        if (scope == "all")
        {
            foreach (var framework in Frameworks)
            {
                scenarios.Add(new ConsumerScenario("core", "LeanCorpus", ["LeanCorpus"], ["LeanCorpus"], "core", framework,
                    "Microsoft.NET.Sdk", string.Empty, [], "core"));
            }
        }

        foreach (var codec in new[] { "LZ4", "Snappy", "Zstandard" })
        {
            var id = $"LeanCorpus.Compression.{codec}";
            foreach (var framework in Frameworks)
            {
                scenarios.Add(new ConsumerScenario($"compression-{codec}", id, ["LeanCorpus", id], ["LeanCorpus", id],
                    $"compression-{codec}", framework, "Microsoft.NET.Sdk", string.Empty, [], "compression"));
            }
        }

        if (scope == "all")
        {
            foreach (var framework in Frameworks)
            {
                scenarios.Add(new ConsumerScenario("text", "Rowles.Text", ["Rowles.Text"], ["Rowles.Text"], "text", framework,
                    "Microsoft.NET.Sdk", string.Empty, [], "single"));
                scenarios.Add(new ConsumerScenario("sourcegen", "LeanCorpus.SourceGen", ["LeanCorpus", "LeanCorpus.SourceGen"],
                    ["LeanCorpus", "LeanCorpus.SourceGen"], "sourcegen", framework, "Microsoft.NET.Sdk", string.Empty, [], "single"));
            }

            var servers = new[]
            {
                new ServerScenario("server-abstractions", "Rowles.LeanCorpus.Server.Abstractions", ["Rowles.LeanCorpus.Server.Abstractions"], "abstractions", "Microsoft.NET.Sdk", "single", [], string.Empty,
                    ["Rowles.LeanCorpus.Server.Abstractions"]),
                new ServerScenario("server-core", "Rowles.LeanCorpus.Server.Core", ["Rowles.LeanCorpus.Server.Core"], "server-core", "Microsoft.NET.Sdk", "single", [], string.Empty,
                    ["Rowles.LeanCorpus.Server.Core", "Rowles.LeanCorpus.Server.Abstractions", "LeanCorpus"]),
                new ServerScenario("server-aspnetcore", "Rowles.LeanCorpus.Server.AspNetCore", ["Rowles.LeanCorpus.Server.AspNetCore"], "server-aspnetcore", "Microsoft.NET.Sdk.Web", "single", [], string.Empty,
                    ["Rowles.LeanCorpus.Server.AspNetCore", "Rowles.LeanCorpus.Server.Core", "Rowles.LeanCorpus.Server.Abstractions", "LeanCorpus"]),
                new ServerScenario("server-grpc", "Rowles.LeanCorpus.Server.Grpc", ["Rowles.LeanCorpus.Server.Grpc"], "grpc", "Microsoft.NET.Sdk.Web", "single", [], string.Empty,
                    ["Rowles.LeanCorpus.Server.Grpc", "Rowles.LeanCorpus.Server.Abstractions"]),
                new ServerScenario("studio", "Rowles.LeanCorpus.Studio", ["Rowles.LeanCorpus.Studio"], "studio", "Microsoft.NET.Sdk.Web", "single", [], "<AddRazorSupportForMvc>true</AddRazorSupportForMvc>",
                    ["Rowles.LeanCorpus.Studio", "Rowles.LeanCorpus.Server.Abstractions"]),
                new ServerScenario("orchard-search", "Rowles.LeanCorpus.OrchardCore.Search", ["Rowles.LeanCorpus.OrchardCore.Search"], "orchard", "Microsoft.NET.Sdk.Web", "single",
                    ["OrchardCore.Application.Cms.Targets"], string.Empty, ["Rowles.LeanCorpus.OrchardCore.Search", "LeanCorpus"]),
                new ServerScenario("server-local", "Rowles.LeanCorpus.Server.Local", ["Rowles.LeanCorpus.Server.Local"], "server-local-launcher", "Microsoft.NET.Sdk", "server-local", [], string.Empty, [])
            };

            foreach (var scenario in servers)
            {
                foreach (var framework in Frameworks)
                {
                    scenarios.Add(new ConsumerScenario(scenario.Name, scenario.PackageId, scenario.PackageIds, scenario.ExactInternalIds,
                        scenario.Kind, framework, scenario.Sdk, scenario.ExtraProperties, scenario.ExternalReferences, scenario.RuntimeKind));
                }
            }
        }

        return scenarios;
    }

    private sealed record ServerScenario(
        string Name,
        string PackageId,
        IReadOnlyList<string> PackageIds,
        string Kind,
        string Sdk,
        string RuntimeKind,
        IReadOnlyList<string> ExternalReferences,
        string ExtraProperties,
        IReadOnlyList<string> ExactInternalIds = default!);
}
