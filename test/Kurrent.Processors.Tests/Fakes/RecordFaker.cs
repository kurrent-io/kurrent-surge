// ReSharper disable ArrangeConstructorOrDestructorBody

using Bogus;
using Humanizer;
using Kurrent.Client;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Tests.Fakes;

/// <summary>
/// A Bogus faker for generating test Record instances with realistic data.
/// </summary>
sealed class RecordFaker : Faker<Record> {
    public RecordFaker() {
        CustomInstantiator(f => {
                var urn = GenerateMessageSchemaUrn(f);

                return new Record {
                    Id             = f.Random.Guid(),
                    Position       = new RecordPosition {
                        LogPosition    = LogPosition.From(f.IndexGlobal + 1),
                        Stream         = StreamName.From(f.Lorem.Slug()),
                        Revision = Revision.From(f.Random.Long(1, 20))
                    },
                    Timestamp      = TimeProvider.System.GetLocalNow().LocalDateTime,
                    Schema         = new RecordSchemaInfo {
                        SchemaName   = urn.ToString(),
                        SchemaFormat = SchemaDataFormat.Json
                    }
                };
            }
        );
    }

    static SchemaUrn GenerateMessageSchemaUrn(Faker faker) {
        var ns          = GenerateMessageNamespace();
        var messageName = GenerateMessageName();

        Guid? schemaId = faker.Random.Bool()
            ? faker.Random.Guid()
            : null;

        return SchemaUrn.Create(ns, messageName, schemaId );


        string GenerateMessageNamespace() {
            var company = faker.Company.CompanyName();
            var domain  = faker.Commerce.Department();

            company = Sanitize(company);
            domain  = Sanitize(domain);

            return $"{company}.{domain}";

            string Sanitize(string input) {
                return input
                    .Replace(" ", "")
                    .Replace(",", "")
                    .Replace("`", "")
                    .Replace("'", "")
                    .Replace("-", "")
                    .Replace("&", "And");
            }
        }

        string GenerateMessageName() {
            var action  = faker.Hacker.Verb().Humanize();
            var noun    = faker.Hacker.Noun().Humanize();
            var version = faker.Random.Int(1, 10);

            return $"{action}{noun}V{version}";
        }
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

        return record with {
            Position = new RecordPosition {
                LogPosition = position.HasValue ? LogPosition.From(position.Value) : record.Position.LogPosition,
                Stream      = stream is { } ? StreamName.From(stream) : record.Position.Stream,
                Revision    = streamRevision.HasValue ? Revision.From(streamRevision.Value) : record.Position.Revision,
                Partition   = record.Position.Partition
            },
            Timestamp = timestamp ?? record.Timestamp,
            Metadata  = metadata ?? record.Metadata,
            Schema = record.Schema with {
                SchemaName = schemaName is { } ? SchemaName.From(schemaName) : record.Schema.SchemaName
            }
        };
    }
}
