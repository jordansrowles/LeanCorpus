namespace Rowles.LeanCorpus.Store;

/// <summary>Sequential primitive output used by streaming codec bodies.</summary>
internal interface ISequentialIndexOutput
{
    long Position { get; }

    void WriteByte(byte value);

    void WriteBoolean(bool value);

    void WriteInt32(int value);

    void WriteInt64(long value);

    void WriteBytes(ReadOnlySpan<byte> data);

    void WriteVarInt(int value);
}
