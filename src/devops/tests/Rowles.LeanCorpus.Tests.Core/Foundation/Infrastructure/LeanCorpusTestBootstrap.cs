using System.Runtime.CompilerServices;
using Rowles.LeanCorpus;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;

namespace Rowles.LeanCorpus.Tests.Core.Foundation.Infrastructure;

/// <summary>Applies the functional-test durability policy when this assembly is loaded.</summary>
internal static class LeanCorpusTestBootstrap
{
    [ModuleInitializer]
    internal static void Initialise()
    {
        Lz4Compression.Register();
        SnappyCompression.Register();
        ZstandardCompression.Register();
        LeanCorpusDefaults.Configure(static options => options.IndexWriter.DurableCommits = false);
    }
}
