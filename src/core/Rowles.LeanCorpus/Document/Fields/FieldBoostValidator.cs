namespace Rowles.LeanCorpus.Document.Fields;

internal static class FieldBoostValidator
{
    internal static float Validate(float boost, string paramName)
    {
        if (!float.IsFinite(boost) || boost <= 0f)
            throw new ArgumentOutOfRangeException(paramName, "Field boosts must be finite and greater than zero.");

        return boost;
    }
}
