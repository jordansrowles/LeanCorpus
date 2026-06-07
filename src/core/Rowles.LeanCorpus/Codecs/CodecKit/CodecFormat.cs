namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// A chain of version steps for one codec format (e.g. "pos", "fdt").
/// <see cref="Steps"/>[0] is the oldest version; <see cref="Steps"/>[^1] is current.
/// Duplicate versions are rejected on construction.
/// </summary>
internal sealed record CodecFormat
{
    /// <summary>
    /// Creates a codec format with the given identifier and ordered version steps.
    /// </summary>
    /// <param name="codecId">The codec identifier (e.g. "pos", "nrm", "fdt").</param>
    /// <param name="steps">Ordered version steps from oldest to newest.</param>
    public CodecFormat(string codecId, IReadOnlyList<CodecVersionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(codecId);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
            throw new ArgumentException("At least one version step is required.", nameof(steps));

        var seen = new HashSet<int>(steps.Count);
        foreach (var step in steps)
        {
            if (!seen.Add(step.Version))
                throw new ArgumentException(
                    $"Duplicate version {step.Version} in codec format '{codecId}'.", nameof(steps));
        }


        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i] is null)
                throw new ArgumentException(
                    $"Version step at index {i} is null in codec format '{codecId}'.", nameof(steps));
        }

        for (var i = 1; i < steps.Count; i++)
        {
            if (steps[i].Version <= steps[i - 1].Version)
                throw new ArgumentException(
                    $"Versions must be strictly increasing from oldest to newest in codec format '{codecId}'.", nameof(steps));
        }
        CodecId = codecId;
        Steps = steps;
    }

    public string CodecId { get; }
    public IReadOnlyList<CodecVersionStep> Steps { get; }
}
