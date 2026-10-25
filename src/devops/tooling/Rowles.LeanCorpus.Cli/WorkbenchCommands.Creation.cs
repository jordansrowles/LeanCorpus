using System.CommandLine;
using System.Text.Json;
using Rowles.LeanCorpus.Cli.Creation;

namespace Rowles.LeanCorpus.Cli;

internal static partial class WorkbenchCommands
{
    private static Command BuildCreateCommands()
    {
        var command = new Command("create", "Create deterministic synthetic LeanCorpus indexes.");
        var preset = new Argument<string>("preset") { Description = "Preset name, for example search-small, vector-hnsw or hybrid-balanced." };
        var path = new Argument<string>("index-path") { Description = "Output index directory. It must be empty or absent." };
        var seed = new Option<ulong>("--seed") { Description = "Deterministic DataForge seed (default 42).", DefaultValueFactory = _ => 42UL };
        var count = new Option<int?>("--count") { Description = "Number of records (preset default when omitted)." };
        var dimension = new Option<int>("--dimension") { Description = "Vector dimension (default 32).", DefaultValueFactory = _ => 32 };
        var distribution = new Option<string>("--distribution") { Description = "Vector distribution (default Clustered).", DefaultValueFactory = _ => "Clustered" };
        var language = new Option<string>("--language") { Description = "Multilingual profile language (default en).", DefaultValueFactory = _ => "en" };
        var json = Flag("--json", "Write a JSON creation report.");
        var plain = Flag("--plain", "Use plain text output.");
        var createPreset = new Command("preset", "Generate a registered DataForge-backed index preset.");
        createPreset.Add(preset);
        createPreset.Add(path);
        createPreset.Add(seed);
        createPreset.Add(count);
        createPreset.Add(dimension);
        createPreset.Add(distribution);
        createPreset.Add(language);
        createPreset.Add(json);
        createPreset.Add(plain);
        createPreset.SetAction(result => RunCreatePreset(
            GetRequiredValue(result, preset), GetRequiredValue(result, path), result.GetValue(seed), result.GetValue(count),
            result.GetValue(dimension), result.GetValue(distribution) ?? "Clustered", result.GetValue(language) ?? "en", result.GetValue(json),
            result.InvocationConfiguration.Output));
        command.Add(createPreset);

        var customPath = new Argument<string>("index-path") { Description = "Output index directory." };
        var profile = new Option<string>("--profile") { Description = "DataForge profile family: search, vector, hybrid or multilingual.", DefaultValueFactory = _ => "search" };
        var schema = new Option<string?>("--schema") { Description = "JSON schema file for custom field types, DocValues, facets, vectors and geo points." };
        var customSeed = new Option<ulong>("--seed") { Description = "Deterministic seed (default 42).", DefaultValueFactory = _ => 42UL };
        var customCount = new Option<int>("--count") { Description = "Number of generated records (default 128).", DefaultValueFactory = _ => 128 };
        var customJson = Flag("--json", "Write a JSON creation report.");
        var custom = new Command("custom", "Create a small custom index from a DataForge profile family.");
        custom.Add(customPath);
        custom.Add(profile);
        custom.Add(schema);
        custom.Add(customSeed);
        custom.Add(customCount);
        custom.Add(customJson);
        custom.SetAction(result => RunCreateCustom(GetRequiredValue(result, customPath), result.GetValue(profile) ?? "search",
            result.GetValue(schema), result.GetValue(customSeed), result.GetValue(customCount), result.GetValue(customJson),
            result.InvocationConfiguration.Output));
        command.Add(custom);

        var sourcePath = new Argument<string>("source-index") { Description = "Index directory containing .leancorpus/recipe.json." };
        var outputPath = new Argument<string>("output-index") { Description = "New output index directory." };
        var reproduce = new Command("reproduce", "Recreate an index from its stored DataForge recipe.");
        reproduce.Add(sourcePath);
        reproduce.Add(outputPath);
        reproduce.Add(json);
        reproduce.SetAction(result => RunReproduce(GetRequiredValue(result, sourcePath), GetRequiredValue(result, outputPath), result.GetValue(json), result.InvocationConfiguration.Output));
        command.Add(reproduce);
        AddWorkloadCreationCommand(command);
        return command;
    }

    private static int RunCreatePreset(string preset, string path, ulong seed, int? count, int dimension,
        string distribution, string language, bool json, TextWriter output)
    {
        IndexCreationResult result = IndexCreator.CreatePreset(preset, path, seed, count, dimension, distribution, language);
        if (json)
            WriteJson(output, result);
        else
            output.WriteLine($"Created {result.ProfileId} index with {result.DocumentCount} documents at {result.IndexPath}. Recipe: {result.RecipeIdentity}");
        return CliExitCodes.Success;
    }

    private static int RunCreateCustom(string path, string profile, string? schemaPath, ulong seed, int count, bool json, TextWriter output)
    {
        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            IndexCreationResult custom = IndexCreator.CreateCustom(schemaPath, path, seed, count);
            if (json)
                WriteJson(output, custom);
            else
                output.WriteLine($"Created custom DataForge index with {custom.DocumentCount} documents at {custom.IndexPath}. Recipe: {custom.RecipeIdentity}");
            return CliExitCodes.Success;
        }
        string preset = profile.Trim().ToLowerInvariant() switch
        {
            "search" => "search-small",
            "vector" => "vector-hnsw",
            "hybrid" => "hybrid-balanced",
            "multilingual" => "multilingual",
            _ => throw new ArgumentException($"Unknown DataForge profile family '{profile}'.")
        };
        IndexCreationResult result = IndexCreator.CreatePreset(preset, path, seed, count, dimension: 32, "Clustered", "en");
        if (json)
            WriteJson(output, result);
        else
            output.WriteLine($"Created {result.ProfileId} index with {result.DocumentCount} documents at {result.IndexPath}. Recipe: {result.RecipeIdentity}");
        return CliExitCodes.Success;
    }

    private static int RunReproduce(string sourcePath, string outputPath, bool json, TextWriter output)
    {
        IndexCreationResult result = IndexCreator.Reproduce(sourcePath, outputPath);
        if (json)
            WriteJson(output, result);
        else
            output.WriteLine($"Recreated {result.ProfileId} index with {result.DocumentCount} documents at {result.IndexPath}. Recipe: {result.RecipeIdentity}");
        return CliExitCodes.Success;
    }
}
