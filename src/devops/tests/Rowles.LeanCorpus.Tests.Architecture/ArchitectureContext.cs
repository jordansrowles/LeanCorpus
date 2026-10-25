using ArchUnitNET.Loader;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Architecture;

internal static class ArchitectureContext
{
    internal static readonly System.Reflection.Assembly CoreAssembly = typeof(LeanDirectory).Assembly;

    internal static readonly ArchUnitNET.Domain.Architecture Core = new ArchLoader()
        .LoadAssembly(CoreAssembly)
        .Build();
}
