using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Tests.Core.CodecKit;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class StoredFieldsBlockPolicyTests
{
    [Fact]
    public void RawLengthRetainsItsIndependentCeiling()
    {
        Assert.Equal(256 * 1024 * 1024, StoredFieldsBlockPolicy.MaximumRawBytes);
        StoredFieldsBlockPolicy.ValidateRawLength(StoredFieldsBlockPolicy.MaximumRawBytes);
        Assert.Throws<InvalidDataException>(() =>
            StoredFieldsBlockPolicy.ValidateRawLength((long)StoredFieldsBlockPolicy.MaximumRawBytes + 1));
    }

    [Fact]
    public void CurrentEncodedLengthHasItsOwnAbsoluteCeiling()
    {
        Assert.Equal(320 * 1024 * 1024, StoredFieldsBlockPolicy.MaximumEncodedBytes);
        StoredFieldsBlockPolicy.ValidateEncodedLength(StoredFieldsBlockPolicy.MaximumRawBytes + 1, 5);
        StoredFieldsBlockPolicy.ValidateEncodedLength(StoredFieldsBlockPolicy.MaximumEncodedBytes, 5);
        Assert.Throws<InvalidDataException>(() =>
            StoredFieldsBlockPolicy.ValidateEncodedLength(StoredFieldsBlockPolicy.MaximumEncodedBytes + 1, 5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void HistoricalEncodedLengthRetainsRawCeiling(int version)
    {
        StoredFieldsBlockPolicy.ValidateEncodedLength(StoredFieldsBlockPolicy.MaximumRawBytes, version);
        Assert.Throws<InvalidDataException>(() =>
            StoredFieldsBlockPolicy.ValidateEncodedLength(StoredFieldsBlockPolicy.MaximumRawBytes + 1, version));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EncodedLengthMustBePositive(int version)
    {
        Assert.Throws<InvalidDataException>(() => StoredFieldsBlockPolicy.ValidateEncodedLength(0, version));
        Assert.Throws<InvalidDataException>(() => StoredFieldsBlockPolicy.ValidateEncodedLength(-1, version));
    }
}
