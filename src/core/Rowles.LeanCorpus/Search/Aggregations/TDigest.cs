namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Mergeable t-digest with a tail-aware, bounded centroid-weight scale constraint.</summary>
public sealed class TDigest
{
    /// <summary>Default compression. Higher values improve tail accuracy at the cost of centroid memory.</summary>
    public const int DefaultCompression = 100;
    private readonly List<Centroid> _centroids = [];

    /// <summary>Initialises a digest with compression from 20 to 1,000.</summary>
    public TDigest(int compression = DefaultCompression)
    {
        if (compression is < 20 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(compression), "t-digest compression must be from 20 to 1,000.");
        Compression = compression;
    }

    /// <summary>Gets the compression setting.</summary>
    public int Compression { get; }
    /// <summary>Gets the total recorded weight.</summary>
    public double Count { get; private set; }

    /// <summary>Adds one finite value.</summary>
    public void Add(double value, double weight = 1)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "t-digest values must be finite.");
        if (!double.IsFinite(weight) || weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
        _centroids.Add(new Centroid(value, weight)); Count += weight;
        if (_centroids.Count > Compression * 8) Compress();
    }

    /// <summary>Compresses pending centroids with tail-aware cluster limits.</summary>
    public void Compress()
    {
        if (_centroids.Count < 2) return;
        _centroids.Sort(static (x, y) => x.Mean.CompareTo(y.Mean));
        var output = new List<Centroid>(_centroids.Count);
        Centroid current = _centroids[0]; double soFar = 0;
        for (int i = 1; i < _centroids.Count; i++)
        {
            var next = _centroids[i];
            double q = (soFar + current.Weight + next.Weight) / Count;
            if (current.Weight + next.Weight <= MaximumWeight(q))
                current = current.Merge(next);
            else { output.Add(current); soFar += current.Weight; current = next; }
        }
        output.Add(current); _centroids.Clear(); _centroids.AddRange(output);
    }

    /// <summary>Returns a quantile in the inclusive range 0 to 1.</summary>
    public double Quantile(double quantile)
    {
        if (quantile is < 0 or > 1 || double.IsNaN(quantile)) throw new ArgumentOutOfRangeException(nameof(quantile));
        if (Count == 0) return 0;
        Compress();
        if (quantile.Equals(0d)) return _centroids[0].Mean;
        if (quantile.Equals(1d)) return _centroids[^1].Mean;
        double target = quantile * Count, cumulative = 0;
        for (int i = 0; i < _centroids.Count; i++)
        {
            var centroid = _centroids[i]; double next = cumulative + centroid.Weight;
            if (target <= next)
            {
                double left = i == 0 ? centroid.Mean : (_centroids[i - 1].Mean + centroid.Mean) / 2;
                double right = i == _centroids.Count - 1 ? centroid.Mean : (centroid.Mean + _centroids[i + 1].Mean) / 2;
                return left + ((target - cumulative) / centroid.Weight) * (right - left);
            }
            cumulative = next;
        }
        return _centroids[^1].Mean;
    }

    /// <summary>Merges another digest with the same compression.</summary>
    public void MergeFrom(TDigest other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Compression != other.Compression) throw new ArgumentException("t-digest compression must match before merging.", nameof(other));
        other.Compress(); _centroids.AddRange(other._centroids); Count += other.Count; Compress();
    }

    private readonly record struct Centroid(double Mean, double Weight)
    {
        public Centroid Merge(Centroid other) => new((Mean * Weight + other.Mean * other.Weight) / (Weight + other.Weight), Weight + other.Weight);
    }

    // This is the cluster-size form of the arcsine t-digest scale: it allows
    // broad centroids near the median and progressively smaller ones in both tails.
    private double MaximumWeight(double quantile)
        => Math.Max(1, 4 * Count * quantile * (1 - quantile) / Compression);
}
