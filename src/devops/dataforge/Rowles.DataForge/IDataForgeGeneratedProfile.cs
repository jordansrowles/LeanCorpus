namespace Rowles.DataForge;


public interface IDataForgeGeneratedProfile<TRecord> : IDataForgeProfile
{
    IEnumerable<TRecord> Generate(DataForgeGenerationOptions options);

    IDataForgeCanonicalRecordWriter<TRecord> CanonicalRecordWriter { get; }

    IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; }

    IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options);
}
