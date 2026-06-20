namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Build-time configuration for HNSW vector graphs.
/// </summary>
public sealed class HnswBuildConfig
{
    private int _m = 16;
    private int _efConstruction = 100;
    private int _m0;

    /// <summary>Maximum neighbours per node on layers above zero. Must be in [2, 100]. Default 16.</summary>
    public int M
    {
        get => _m;
        init
        {
            if (value is < 2 or > 100)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "M must be between 2 and 100 inclusive. Values above 100 produce diminishing recall gains with quadratic construction cost.");
            _m = value;
        }
    }

    /// <summary>Candidate set size during graph construction. Must be in [1, 2000]. Default 100.</summary>
    public int EfConstruction
    {
        get => _efConstruction;
        init
        {
            if (value is < 1 or > 2000)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "EfConstruction must be between 1 and 2000 inclusive. Values above 2000 produce extreme construction-time overhead.");
            _efConstruction = value;
        }
    }

    /// <summary>
    /// Maximum neighbours on layer zero. Defaults to <c>2 * M</c> when zero.
    /// Must be in [2, 200] when set explicitly.
    /// </summary>
    public int M0
    {
        get => _m0;
        init
        {
            if (value != 0 && (value < 2 || value > 200))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "M0 must be 0 (use default) or between 2 and 200 inclusive.");
            _m0 = value;
        }
    }

    /// <summary>Effective layer-zero degree.</summary>
    internal int EffectiveM0 => M0 > 0 ? M0 : 2 * M;
}
