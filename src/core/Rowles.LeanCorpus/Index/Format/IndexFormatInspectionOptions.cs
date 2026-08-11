using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Index.Format;

/// <summary>
/// Options for inspecting the on-disk format of a LeanCorpus index.
/// </summary>
public sealed class IndexFormatInspectionOptions
{
    /// <summary>Gets or sets the immutable codec catalogue used to identify persistent formats.</summary>
    public CodecCatalog Catalog { get; set; } = CodecCatalog.Default;

    /// <summary>Gets or sets whether optional sidecar files are included. Defaults to <c>true</c>.</summary>
    public bool IncludeOptionalSidecars { get; set; } = true;

    /// <summary>Gets or sets whether file lengths are included. Defaults to <c>true</c>.</summary>
    public bool IncludeFileSizes { get; set; } = true;

    /// <summary>Gets or sets whether checksums are included. Defaults to <c>false</c>.</summary>
    public bool IncludeChecksums { get; set; }
}
