using System.Text.Json;

namespace Rowles.LeanCorpus.Server.Core.Storage;

/// <summary>Provides atomic persistence for the local index registry.</summary>
internal sealed class RegistryStore
{
    internal const int CurrentFormatVersion = 1;
    private const string RegistryFileName = "registry.json";
    private readonly string _registryPath;

    internal RegistryStore(string dataRoot)
    {
        _registryPath = Path.Combine(dataRoot, RegistryFileName);
    }

    internal async ValueTask<ServerRegistry> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath))
            return new ServerRegistry([], CurrentFormatVersion);

        ServerRegistry registry;
        try
        {
            await using FileStream stream = File.OpenRead(_registryPath);
            registry = await JsonSerializer.DeserializeAsync(stream, RegistryJsonSerialiserContext.Default.ServerRegistry, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The server registry is empty or invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The server registry contains invalid JSON.", exception);
        }

        if (registry.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException($"The server registry format version '{registry.FormatVersion}' is not supported. Expected {CurrentFormatVersion}.");
        return registry;
    }

    internal async ValueTask SaveAsync(ServerRegistry registry, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_registryPath)!;
        string temporaryPath = Path.Combine(directory, $".{RegistryFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, registry, RegistryJsonSerialiserContext.Default.ServerRegistry, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _registryPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
