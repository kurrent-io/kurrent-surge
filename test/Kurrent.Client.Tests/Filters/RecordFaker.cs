using Bogus;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Filters;

/// <summary>
/// A Bogus faker for generating test Record instances with realistic data.
/// </summary>
public class RecordFaker : Faker<Record> {
    public RecordFaker() {
        CustomInstantiator(f => {
                var company    = f.Company.CompanyName().Replace(" ", "").Replace(",", "").Replace("-", "");
                var domain     = f.Commerce.Department().Replace(" ", "");
                var action     = f.Hacker.Verb().Replace(" ", "");
                var noun       = f.Hacker.Noun().Replace(" ", "");
                var version    = f.Random.Int(1, 5);
                var schemaName = $"{company}.{domain}.{action}{noun}V{version}";

                var meta = new Metadata()
                    .With(SystemMetadataKeys.SchemaName, SchemaName.From(schemaName))
                    .With(SystemMetadataKeys.SchemaFormat, "json");

                return new Record {
                    Id             = f.Random.Guid(),
                    Stream         = f.Lorem.Slug(),
                    Position       = f.Random.Long(0, 10000),
                    StreamRevision = f.Random.Long(0, 1000),
                    Timestamp      = f.Date.Recent(),
                    Metadata       = meta
                };
            }
        );
    }

    /// <summary>
    /// Creates a record with specific values, using defaults for unspecified properties.
    /// </summary>
    public Record With(
        string? stream = null,
        string? schemaName = null,
        DateTime? timestamp = null,
        long? position = null,
        long? streamRevision = null,
        Metadata? metadata = null
    ) {
        var record = Generate();

        var meta = metadata ?? new Metadata();
        meta = meta
            .With(SystemMetadataKeys.SchemaName, SchemaName.From(schemaName ?? record.Schema.SchemaName.Value))
            .With(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Json);

        return record with {
            Stream = stream ?? record.Stream.Value,
            Position = position ?? record.Position.Value,
            StreamRevision = streamRevision ?? (long)record.StreamRevision,
            Timestamp = timestamp ?? record.Timestamp,
            Metadata = meta
        };
    }
}
