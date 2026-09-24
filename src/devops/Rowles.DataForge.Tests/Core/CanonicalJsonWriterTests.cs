using System.Globalization;
using System.Text;
using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class CanonicalJsonWriterTests
{
    [Fact]
    public void Strings_escape_quotes_backslashes_and_controls_in_lowercase_hex()
    {
        var bytes = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("value");
            writer.WriteStringValue("\"\\\0\u001f");
            writer.WriteEndObject();
            writer.WriteLine();
        });

        Assert.Equal(Encoding.UTF8.GetBytes("""{"value":"\"\\\u0000\u001f"}""" + "\n"), bytes);
    }

    [Fact]
    public void Valid_non_ascii_scalars_are_written_as_raw_utf8()
    {
        const string value = "café 東京 مرحبا";
        var bytes = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("text");
            writer.WriteStringValue(value);
            writer.WriteEndObject();
            writer.WriteLine();
        });

        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal("{\"text\":\"café 東京 مرحبا\"}\n", text);
        Assert.DoesNotContain("\\u", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unpaired_surrogate_is_rejected()
    {
        using var stream = new MemoryStream();
        using var writer = new CanonicalJsonWriter(stream);
        writer.WriteStartObject();
        writer.WritePropertyName("bad");
        Assert.Throws<ArgumentException>(() => writer.WriteStringValue("\ud800"));
    }

    [Fact]
    public void Integer_boundaries_use_invariant_decimal_text()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var bytes = Write(writer =>
            {
                writer.WriteStartArray();
                writer.WriteInt32Value(int.MinValue);
                writer.WriteInt32Value(int.MaxValue);
                writer.WriteInt64Value(long.MinValue);
                writer.WriteInt64Value(long.MaxValue);
                writer.WriteUInt64Value(ulong.MaxValue);
                writer.WriteEndArray();
                writer.WriteLine();
            });
            Assert.Equal("[-2147483648,2147483647,-9223372036854775808,9223372036854775807,18446744073709551615]\n", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Property_order_empty_strings_and_final_lf_are_preserved()
    {
        var bytes = Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ordinal");
            writer.WriteInt64Value(0);
            writer.WritePropertyName("body");
            writer.WriteStringValue(string.Empty);
            writer.WriteEndObject();
            writer.WriteLine();
        });

        Assert.Equal("{\"ordinal\":0,\"body\":\"\"}\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Fact]
    public void Vector_float_bits_use_little_endian_words_then_base64()
    {
        var bytes = Write(writer =>
        {
            writer.WriteStartArray();
            writer.WriteSingleBitsBase64Value([1.0f, -2.5f]);
            writer.WriteEndArray();
            writer.WriteLine();
        });

        Assert.Equal("[\"AACAPwAAIMA=\"]\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Timestamps_use_utc_and_seven_fractional_digits()
    {
        var value = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.FromHours(2)).AddTicks(1_234_567);
        var bytes = Write(writer =>
        {
            writer.WriteStartArray();
            writer.WriteUtcTimestampValue(value);
            writer.WriteEndArray();
            writer.WriteLine();
        });

        Assert.Equal("[\"2024-02-03T02:05:06.1234567Z\"]\n", Encoding.UTF8.GetString(bytes));
    }

    private static byte[] Write(Action<CanonicalJsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new CanonicalJsonWriter(stream))
            action(writer);
        return stream.ToArray();
    }
}
