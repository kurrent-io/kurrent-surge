// ReSharper disable ArrangeConstructorOrDestructorBody

using Bogus;
using Humanizer;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Tests.Filters;

/// <summary>
/// A Bogus faker for generating test Record instances with realistic data.
/// </summary>
sealed class RecordFaker : Faker<Record> {
    public RecordFaker() {
        CustomInstantiator(f => {
                var urn = GenerateMessageSchemaUrn(f);

                return new Record {
                    Id             = Guid.NewGuid(),
                    Position       = new RecordPosition {
                        LogPosition    = LogPosition.From(f.IndexGlobal + 1),
                        StreamPosition = new StreamPosition(f.Lorem.Slug(), f.Random.Long(1, 20))
                    },
                    Timestamp      = TimeProvider.System.GetLocalNow().LocalDateTime,
                    Schema         = new RecordSchemaInfo {
                        Schema   = urn.ToString(),
                        DataFormat = SchemaDataFormat.Json
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
                StreamPosition = new StreamPosition(
                    StreamName.From(stream ?? record.Position.Stream),
                    Revision.From(streamRevision ?? record.Position.Revision)),
                Partition   = record.Position.Partition
            },
            Timestamp = timestamp ?? record.Timestamp,
            Metadata  = metadata ?? record.Metadata,
            Schema = record.Schema with {
                Schema = schemaName is { } ? SchemaName.From(schemaName) : record.Schema.Schema
            }
        };
    }
}


// using Bogus;
// using Humanizer;
// using Kurrent.Surge.Client.Registry;
// using Kurrent.Surge.Client.Schema.NameStrategies;
// using Kurrent.Surge.Client.Streams;
//
// namespace Kurrent.Surge.Tests.Filters;
//
// /// <summary>
// /// A Bogus faker for generating test Record instances with realistic data.
// /// </summary>
// class RecordFaker : Faker<Record> {
//     public RecordFaker() {
//         CustomInstantiator(f => {
//                 var urn = GenerateMessageSchemaUrn();
//
//                 return new Record {
//                     Id             = f.Random.Guid(),
//                     Position       = new RecordPosition {
//                         LogPosition    = LogPosition.From(f.IndexGlobal + 1),
//                         Stream         = StreamName.From(f.Lorem.Slug()),
//                         Revision = Revision.From(f.Random.Long(1, 20))
//                     },
//                     Timestamp      = TimeProvider.System.GetLocalNow().LocalDateTime,
//                     Schema         = new RecordSchemaInfo {
//                         SchemaName   = urn.ToString(),
//                         SchemaFormat = SchemaDataFormat.Json
//                     }
//                 };
//             }
//         );
//     }
//
//     static SchemaUrn GenerateMessageSchemaUrn() {
//         var ns          = GenerateMessageNamespace();
//         var messageName = GenerateMessageName();
//
//         Guid? schemaId = BogusFaker.Shared.Random.Bool()
//             ? BogusFaker.Shared.Random.Guid()
//             : null;
//
//         return SchemaUrn.Create(ns, messageName, schemaId );
//     }
//
//     static string GenerateMessageNamespace() {
//         var company = BogusFaker.Shared.Company.CompanyName();
//         var domain  = BogusFaker.Shared.Commerce.Department();
//
//         company = Sanitize(company);
//         domain  = Sanitize(domain);
//
//         return $"{company}.{domain}";
//
//         string Sanitize(string input) {
//             return input
//                 .Replace(" ", "")
//                 .Replace(",", "")
//                 .Replace("`", "")
//                 .Replace("'", "")
//                 .Replace("-", "")
//                 .Replace("&", "And");
//         }
//     }
//
//     static string GenerateMessageName() {
//         var action  = BogusFaker.Shared.Hacker.Verb().Humanize();
//         var noun    = BogusFaker.Shared.Hacker.Noun().Humanize();
//         var version = BogusFaker.Shared.Random.Int(1, 10);
//
//         return $"{action}{noun}V{version}";
//     }
//
//     /// <summary>
//     /// Creates a record with specific values, using defaults for unspecified properties.
//     /// </summary>
//     public Record With(
//         string? stream = null,
//         string? schemaName = null,
//         DateTime? timestamp = null,
//         long? position = null,
//         long? streamRevision = null,
//         Metadata? metadata = null
//     ) {
//         var record = Generate();
//
//         return record with {
//             Position = new RecordPosition {
//                 LogPosition    = position.HasValue ? LogPosition.From(position.Value) : record.Position.LogPosition,
//                 Stream         = stream is not null ? StreamName.From(stream) : record.Position.Stream,
//                 Revision = streamRevision.HasValue ? Revision.From(streamRevision.Value) : record.Position.Revision,
//                 Partition      = record.Position.Partition
//             },
//             Timestamp = timestamp ?? record.Timestamp,
//             Metadata  = metadata ?? record.Metadata,
//             Schema = record.Schema with {
//                 SchemaName = schemaName is not null ? SchemaName.From(schemaName) : record.Schema.SchemaName
//             }
//         };
//     }
// }
