using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;

namespace Rowles.DataForge.Tests.Infrastructure;

/// <summary>Registers extension codecs before DataForge tests open an index.</summary>
internal static class DataForgeTestBootstrap
{
    [ModuleInitializer]
    internal static void Initialise()
    {
        Lz4Compression.Register();
        SnappyCompression.Register();
        ZstandardCompression.Register();
    }
}
