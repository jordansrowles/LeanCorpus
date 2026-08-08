using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Owns the engine resources for one explicitly registered local index.</summary>
internal sealed class IndexRuntime : IDisposable
{
    private readonly MMapDirectory _directory;

    internal IndexRuntime(string path)
    {
        _directory = new MMapDirectory(path);
        Writer = new IndexWriter(_directory, new IndexWriterConfig());
        Searchers = new SearcherManager(_directory);
    }

    internal IndexWriter Writer { get; }

    internal SearcherManager Searchers { get; }

    internal string Path => _directory.DirectoryPath;

    public void Dispose()
    {
        Searchers.Dispose();
        Writer.Dispose();
        _directory.Dispose();
    }
}
