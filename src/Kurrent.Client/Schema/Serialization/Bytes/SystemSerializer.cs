using System.Text;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Schema.Serialization.Bytes;

#pragma warning disable CS8509
public class SystemSerializer : ISchemaSerializer {
    public SchemaDataFormat DataFormat => SchemaDataFormat.Bytes;

    public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SchemaSerializationContext context) =>
        throw new NotImplementedException();
    
    static readonly SchemaName LinkSchemaName = "$>";
    
    public ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, SchemaSerializationContext context) {
        if (context.RecordSchema.SchemaName == LinkSchemaName) {
            var linkData = Encoding.UTF8.GetString(data.Span);
            
            var separatorIndex = linkData.IndexOf('@');
            if (separatorIndex == -1)
                throw new FormatException($"Invalid link data format: '{linkData}'. Expected '{{revision}}@{{stream}}'.");

            var revision = Revision.Parse(linkData[..separatorIndex]);
            var stream   = StreamName.From(linkData[(separatorIndex + 1)..]);
            var linkTo   = new LinkTo(stream, revision);
            
            return new(linkTo);
        }

        return ValueTask.FromResult<object?>(ReadOnlyMemory<byte>.Empty);
    }
}