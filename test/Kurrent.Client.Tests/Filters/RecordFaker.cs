using Bogus;
using Humanizer;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Streams;
using Kurrent.Testing.Bogus;

namespace Kurrent.Client.Tests.Filters;

/// <summary>
/// A Bogus faker for generating test Record instances with realistic data.
/// </summary>
class RecordFaker : Faker<Record> {
    public RecordFaker() {
        CustomInstantiator(f => {
                var urn = GenerateMessageSchemaUrn();

                return new Record {
                    Id             = f.Random.Guid(),
                    Stream         = f.Lorem.Slug(),
                    Position       = f.IndexGlobal + 1, //.IndexGlobal.Random.Long(0, 10000),
                    StreamRevision = f.Random.Long(1, 20),
                    Timestamp      = TimeProvider.System.GetLocalNow().LocalDateTime,
                    Schema         = new RecordSchemaInfo {
                        SchemaName   = urn.ToString(),
                        SchemaFormat = SchemaDataFormat.Json
                    }
                };
            }
        );
    }

    static SchemaUrn GenerateMessageSchemaUrn() {
        var ns          = GenerateMessageNamespace();
        var messageName = GenerateMessageName();

        Guid? schemaId = BogusFaker.Shared.Random.Bool()
            ? BogusFaker.Shared.Random.Guid()
            : null;

        return SchemaUrn.Create(ns, messageName, schemaId );
    }

    static string GenerateMessageNamespace() {
        var company = BogusFaker.Shared.Company.CompanyName();
        var domain  = BogusFaker.Shared.Commerce.Department();

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

    static string GenerateMessageName() {
        var action  = BogusFaker.Shared.Hacker.Verb().Humanize();
        var noun    = BogusFaker.Shared.Hacker.Noun().Humanize();
        var version = BogusFaker.Shared.Random.Int(1, 10);

        return $"{action}{noun}V{version}";
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
            Stream         = stream ?? record.Stream.Value,
            Position       = position ?? record.Position.Value,
            StreamRevision = streamRevision ?? (long)record.StreamRevision,
            Timestamp      = timestamp ?? record.Timestamp,
            Metadata       = metadata ?? record.Metadata,
            Schema = record.Schema with {
                SchemaName = schemaName is not null ? SchemaName.From(schemaName) : record.Schema.SchemaName
            }
        };
    }
}
