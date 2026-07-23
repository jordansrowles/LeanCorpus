using System.Text;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Tests.Unit.Index;

public sealed class PendingDeleteQueueTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] Prefix(string field) => Encoding.UTF8.GetBytes(field + '\0');

    [Fact]
    public void Distinct_terms_in_one_field_produce_same_number_of_entries()
    {
        var q = new PendingDeleteQueue();
        for (int i = 0; i < 100; i++)
            q.Queue(0, Utf8($"term-{i}"), Prefix("f"), isSoftDelete: false);

        Assert.Equal(100, q.Count);
        var list = q.GetOrderedList();
        Assert.Equal(100, list.Count);

        // Verify UTF-8 payloads are preserved
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(0, list[i].FieldOrdinal);
            Assert.Equal($"term-{i}", Encoding.UTF8.GetString(list[i].TermUtf8));
        }
    }

    [Fact]
    public void Same_term_queued_repeatedly_produces_one_entry()
    {
        var q = new PendingDeleteQueue();
        for (int i = 0; i < 50; i++)
            q.Queue(0, Utf8("dup"), Prefix("f"), isSoftDelete: false);

        Assert.Equal(1, q.Count);
        Assert.Single(q.GetOrderedList());
    }

    [Fact]
    public void Duplicate_hard_delete_does_not_change_the_queue()
    {
        var q = new PendingDeleteQueue();

        Assert.True(q.Queue(0, Utf8("dup"), Prefix("f"), isSoftDelete: false));
        Assert.False(q.Queue(0, Utf8("dup"), Prefix("f"), isSoftDelete: false));
        Assert.Single(q.GetOrderedList());
    }

    [Fact]
    public void Soft_then_hard_produces_one_hard_entry()
    {
        var q = new PendingDeleteQueue();
        q.Queue(0, Utf8("term"), Prefix("f"), isSoftDelete: true);
        q.Queue(0, Utf8("term"), Prefix("f"), isSoftDelete: false);

        Assert.Equal(1, q.Count);
        var list = q.GetOrderedList();
        Assert.False(list[0].IsSoftDelete);
    }

    [Fact]
    public void Hard_then_soft_remains_one_hard_entry()
    {
        var q = new PendingDeleteQueue();
        q.Queue(0, Utf8("term"), Prefix("f"), isSoftDelete: false);
        q.Queue(0, Utf8("term"), Prefix("f"), isSoftDelete: true);

        Assert.Equal(1, q.Count);
        var list = q.GetOrderedList();
        Assert.False(list[0].IsSoftDelete);
    }

    [Fact]
    public void Identical_bytes_in_different_field_ordinals_remain_separate()
    {
        var q = new PendingDeleteQueue();
        q.Queue(0, Utf8("shared"), Prefix("a"), isSoftDelete: false);
        q.Queue(1, Utf8("shared"), Prefix("b"), isSoftDelete: false);

        Assert.Equal(2, q.Count);
        var list = q.GetOrderedList();
        Assert.Equal(0, list[0].FieldOrdinal);
        Assert.Equal(1, list[1].FieldOrdinal);
    }

    [Fact]
    public void Unicode_terms_deduplicate_by_utf8_content_not_byte_array_identity()
    {
        var q = new PendingDeleteQueue();
        byte[] a = Utf8("café");
        byte[] b = Utf8("café"); // Different array, same content

        q.Queue(0, a, Prefix("f"), isSoftDelete: false);
        q.Queue(0, b, Prefix("f"), isSoftDelete: false);

        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Clear_removes_both_list_and_index()
    {
        var q = new PendingDeleteQueue();
        q.Queue(0, Utf8("term"), Prefix("f"), isSoftDelete: false);
        Assert.Equal(1, q.Count);

        q.Clear();
        Assert.Equal(0, q.Count);
        Assert.Empty(q.GetOrderedList());

        // Re-queueing a previously cleared term must work
        q.Queue(0, Utf8("term"), Prefix("f"), isSoftDelete: false);
        Assert.Equal(1, q.Count);
        Assert.Single(q.GetOrderedList());
    }
}
