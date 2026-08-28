using System.Runtime.CompilerServices;
using Rowles.LeanCorpus;

namespace Rowles.LeanCorpus.Tests.Core.Foundation.Infrastructure;

/// <summary>Applies the functional-test durability policy when this assembly is loaded.</summary>
internal static class LeanCorpusTestBootstrap
{
    [ModuleInitializer]
    internal static void Initialise()
    {
        LeanCorpusDefaults.Configure(static options => options.IndexWriter.DurableCommits = false);
    }
}
