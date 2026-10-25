namespace Rowles.DataForge;


public interface IDataForgeCanonicalRecordWriter<in TRecord>
{
    void Write(CanonicalJsonWriter writer, TRecord record);
}
