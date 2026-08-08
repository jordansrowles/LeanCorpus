using System.Globalization;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Produces a double value for a document, optionally using its query score.</summary>
public abstract class DoubleValuesSource
{
    /// <summary>A source that returns the current query score.</summary>
    public static DoubleValuesSource Scores { get; } = new ScoreValuesSource();

    /// <summary>Gets whether this source needs the current query score.</summary>
    public abstract bool NeedsScores { get; }

    /// <summary>Gets a value for one global document identifier.</summary>
    public abstract bool TryGetValue(
        IndexSearcher searcher,
        int docId,
        double score,
        out double value);

    /// <summary>Specialises this source for a searcher snapshot.</summary>
    public virtual DoubleValuesSource Rewrite(IndexSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        return this;
    }

    /// <summary>Creates a source backed by a double-valued numeric field.</summary>
    public static DoubleValuesSource FromDoubleField(string field)
        => new DoubleFieldValuesSource(field);

    /// <summary>Creates a source backed by a 64-bit integer field.</summary>
    public static DoubleValuesSource FromInt64Field(string field)
        => new Int64FieldValuesSource(field);

    /// <summary>Creates a source that always returns the supplied value.</summary>
    public static DoubleValuesSource Constant(double value)
        => new ConstantValuesSource(value);

    /// <summary>Adds this source to another source.</summary>
    public DoubleValuesSource Add(DoubleValuesSource other)
        => new BinaryValuesSource(this, other, DoubleValuesOperation.Add);

    /// <summary>Subtracts another source from this source.</summary>
    public DoubleValuesSource Subtract(DoubleValuesSource other)
        => new BinaryValuesSource(this, other, DoubleValuesOperation.Subtract);

    /// <summary>Multiplies this source by another source.</summary>
    public DoubleValuesSource Multiply(DoubleValuesSource other)
        => new BinaryValuesSource(this, other, DoubleValuesOperation.Multiply);

    /// <summary>Divides this source by another source.</summary>
    public DoubleValuesSource Divide(DoubleValuesSource other)
        => new BinaryValuesSource(this, other, DoubleValuesOperation.Divide);

    private sealed class DoubleFieldValuesSource : DoubleValuesSource
    {
        internal string Field { get; }

        internal DoubleFieldValuesSource(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Field must be a non-empty value.", nameof(field));
            Field = field;
        }

        public override bool NeedsScores => false;

        public override bool TryGetValue(
            IndexSearcher searcher,
            int docId,
            double score,
            out double value)
            => searcher.TryResolveNumericValue(docId, Field, out value);

        public override bool Equals(object? obj)
            => obj is DoubleFieldValuesSource other
                && string.Equals(Field, other.Field, StringComparison.Ordinal);

        public override int GetHashCode()
            => HashCode.Combine(nameof(DoubleFieldValuesSource), Field);

        public override string ToString() => $"double({Field})";
    }

    private sealed class Int64FieldValuesSource : DoubleValuesSource
    {
        private readonly string _field;

        internal Int64FieldValuesSource(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Field must be a non-empty value.", nameof(field));
            _field = field;
        }

        public override bool NeedsScores => false;

        public override bool TryGetValue(
            IndexSearcher searcher,
            int docId,
            double score,
            out double value)
        {
            if (searcher.TryResolveInt64Value(docId, _field, out long int64))
            {
                value = int64;
                return true;
            }

            value = 0;
            return false;
        }

        public override bool Equals(object? obj)
            => obj is Int64FieldValuesSource other
                && string.Equals(_field, other._field, StringComparison.Ordinal);

        public override int GetHashCode()
            => HashCode.Combine(nameof(Int64FieldValuesSource), _field);

        public override string ToString() => $"int64({_field})";
    }

    private sealed class ConstantValuesSource : DoubleValuesSource
    {
        private readonly double _value;

        internal ConstantValuesSource(double value) => _value = value;

        public override bool NeedsScores => false;

        public override bool TryGetValue(
            IndexSearcher searcher,
            int docId,
            double score,
            out double value)
        {
            value = _value;
            return true;
        }

        public override bool Equals(object? obj)
            => obj is ConstantValuesSource other && _value.Equals(other._value);

        public override int GetHashCode()
            => HashCode.Combine(nameof(ConstantValuesSource), _value);

        public override string ToString()
            => $"constant({_value.ToString("R", CultureInfo.InvariantCulture)})";
    }

    private sealed class ScoreValuesSource : DoubleValuesSource
    {
        public override bool NeedsScores => true;

        public override bool TryGetValue(
            IndexSearcher searcher,
            int docId,
            double score,
            out double value)
        {
            value = score;
            return true;
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => HashCode.Combine(nameof(ScoreValuesSource));
        public override string ToString() => "scores";
    }

    private sealed class BinaryValuesSource : DoubleValuesSource
    {
        private readonly DoubleValuesSource _left;
        private readonly DoubleValuesSource _right;
        private readonly DoubleValuesOperation _operation;

        internal BinaryValuesSource(
            DoubleValuesSource left,
            DoubleValuesSource right,
            DoubleValuesOperation operation)
        {
            _left = left ?? throw new ArgumentNullException(nameof(left));
            _right = right ?? throw new ArgumentNullException(nameof(right));
            _operation = operation;
        }

        public override bool NeedsScores => _left.NeedsScores || _right.NeedsScores;

        public override bool TryGetValue(
            IndexSearcher searcher,
            int docId,
            double score,
            out double value)
        {
            if (!_left.TryGetValue(searcher, docId, score, out double left)
                || !_right.TryGetValue(searcher, docId, score, out double right))
            {
                value = 0;
                return false;
            }

            value = _operation switch
            {
                DoubleValuesOperation.Add => left + right,
                DoubleValuesOperation.Subtract => left - right,
                DoubleValuesOperation.Multiply => left * right,
                DoubleValuesOperation.Divide => left / right,
                _ => left
            };
            return true;
        }

        public override DoubleValuesSource Rewrite(IndexSearcher searcher)
        {
            ArgumentNullException.ThrowIfNull(searcher);
            var left = _left.Rewrite(searcher);
            var right = _right.Rewrite(searcher);
            return ReferenceEquals(left, _left) && ReferenceEquals(right, _right)
                ? this
                : new BinaryValuesSource(left, right, _operation);
        }

        public override bool Equals(object? obj)
            => obj is BinaryValuesSource other
                && _operation == other._operation
                && _left.Equals(other._left)
                && _right.Equals(other._right);

        public override int GetHashCode()
            => HashCode.Combine(nameof(BinaryValuesSource), _left, _right, _operation);

        public override string ToString() => $"{_operation.ToString().ToLowerInvariant()}({_left},{_right})";
    }

    private enum DoubleValuesOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }
}
