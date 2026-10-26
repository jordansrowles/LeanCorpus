using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record PlaywrightRuntimeInfo(
    string TargetKey,
    string PackageVersion,
    string ChromiumVersion,
    string ChromiumExecutablePath);

internal interface IPlaywrightRuntimePreflight
{
    Task<IReadOnlyList<PlaywrightRuntimeInfo>> ValidateAsync(
        CheckoutIdentity checkout,
        IReadOnlyList<PreparedTestTarget> targets,
        CancellationToken cancellationToken = default);
}

internal interface IPlaywrightRuntimeSessionFactory
{
    Task<IPlaywrightRuntimeSession> CreateAsync(string assemblyPath, CancellationToken cancellationToken = default);
}

internal interface IPlaywrightRuntimeSession : IAsyncDisposable
{
    string ChromiumExecutablePath { get; }
    Task<string> LaunchHeadlessAsync(CancellationToken cancellationToken = default);
}

internal sealed class PlaywrightRuntimePreflight(IPlaywrightRuntimeSessionFactory sessionFactory) : IPlaywrightRuntimePreflight
{
    private const string PlaywrightPackageId = "Microsoft.Playwright";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public async Task<IReadOnlyList<PlaywrightRuntimeInfo>> ValidateAsync(
        CheckoutIdentity checkout,
        IReadOnlyList<PreparedTestTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(sessionFactory);

        var browserTargets = targets.Where(static target => target.Target.RequiresBrowser).ToArray();
        if (browserTargets.Length == 0)
        {
            return [];
        }

        var packageVersion = ReadPinnedPackageVersion(checkout.RepositoryRoot);
        var packageIdentity = $"{PlaywrightPackageId}/{packageVersion}";
        var validatedOutputDirectories = new HashSet<string>(PathComparer);
        var results = new List<PlaywrightRuntimeInfo>();

        foreach (var target in browserTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!target.Target.RunnerKind.Equals("Mtp", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Browser-required target '{target.Target.Key}' must use the MTP runner.");
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(target.ExecutablePath));
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException(
                    $"Could not resolve the output directory for browser-required target '{target.Target.Key}'.");
            }
            if (!validatedOutputDirectories.Add(outputDirectory))
            {
                continue;
            }

            try
            {
                var assemblyPath = Path.Combine(outputDirectory, PlaywrightPackageId + ".dll");
                if (!File.Exists(assemblyPath))
                {
                    throw new InvalidOperationException(
                        $"Microsoft.Playwright.dll is missing from the built target: {assemblyPath}");
                }

                ValidateResolvedPackageIdentity(outputDirectory, packageIdentity);
                await using var session = await sessionFactory.CreateAsync(assemblyPath, cancellationToken).ConfigureAwait(false);
                var executablePath = session.ChromiumExecutablePath;
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    throw new InvalidOperationException(
                        $"The Chromium executable required by Microsoft.Playwright {packageVersion} is missing: {executablePath}");
                }

                var chromiumVersion = await session.LaunchHeadlessAsync(cancellationToken).ConfigureAwait(false);
                results.Add(new PlaywrightRuntimeInfo(target.Target.Key, packageVersion, chromiumVersion, executablePath));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Playwright Chromium preflight failed for '{outputDirectory}': {exception.Message}", exception);
            }
        }

        return results;
    }

    private static string ReadPinnedPackageVersion(string repositoryRoot)
    {
        var packageFile = Path.Combine(repositoryRoot, "Directory.Packages.props");
        if (!File.Exists(packageFile))
        {
            throw new InvalidOperationException($"Central package version file is missing: {packageFile}");
        }

        XDocument document;
        try
        {
            document = XDocument.Load(packageFile);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException($"Could not read central package versions from '{packageFile}'.", exception);
        }

        var versionNodes = document.Descendants()
            .Where(static element => element.Name.LocalName == "PackageVersion" &&
                (string?)element.Attribute("Include") == PlaywrightPackageId)
            .ToArray();
        if (versionNodes.Length != 1 || string.IsNullOrWhiteSpace((string?)versionNodes[0].Attribute("Version")))
        {
            throw new InvalidOperationException(
                "Directory.Packages.props must define one pinned Microsoft.Playwright version.");
        }

        return (string)versionNodes[0].Attribute("Version")!;
    }

    private static void ValidateResolvedPackageIdentity(string outputDirectory, string expectedIdentity)
    {
        var resolvedManifests = new List<string>();
        foreach (var manifestPath in Directory.EnumerateFiles(outputDirectory, "*.deps.json", SearchOption.TopDirectoryOnly))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!manifest.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (libraries.EnumerateObject().Any(property =>
                    property.Name.Equals(expectedIdentity, StringComparison.OrdinalIgnoreCase)))
            {
                resolvedManifests.Add(manifestPath);
            }
        }

        if (resolvedManifests.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one built dependency manifest resolving '{expectedIdentity}' under '{outputDirectory}', found {resolvedManifests.Count}.");
        }
    }
}
