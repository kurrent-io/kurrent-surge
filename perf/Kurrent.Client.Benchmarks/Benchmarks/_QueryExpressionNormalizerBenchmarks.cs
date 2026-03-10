// using System.Collections.Immutable;
// using System.Text;
// using BenchmarkDotNet.Attributes;
// using DotNext.Buffers;
// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Benchmarks.Benchmarks;
//
// [MemoryDiagnoser]
// [Config(typeof(BenchmarkConfig))]
// public class LegacyQueryExpressionNormalizerBenchmarks {
//     LegacyQueryExpressionNormalizer _normalizer = null!;
//     char[] _buffer = null!;
//
//     [ParamsSource(nameof(Scenarios))]
//     public NormalizerScenario Scenario { get; set; } = null!;
//
//     [GlobalSetup]
//     public void Setup() {
//         _normalizer = new LegacyQueryExpressionNormalizer(Scenario.Options);
//         _buffer     = new char[Scenario.Query.Length * 2];
//     }
//
//     [Benchmark]
//     public bool Normalize() {
//         var result = _normalizer.Normalize(Scenario.Query, _buffer);
//         return result.IsValid;
//     }
//
//     public static IEnumerable<NormalizerScenario> Scenarios =>
//         CreateScenarios();
//
//     static IEnumerable<NormalizerScenario> CreateScenarios() {
//         return [
//             #region 1 column, 1 table
//
//             new("1c1t-select", SmallMap,
//                 """
//                 SELECT
//                   schema
//                 FROM records
//                 """),
//
//             new("1c1t-where", SmallMap,
//                 """
//                 SELECT
//                   schema
//                 FROM records
//                 WHERE schema = 'urn:com:example:domain:ordering:events:order-created:v2'
//                 AND position > 100
//                 """),
//
//             new("1c1t-cte-join", SmallMap,
//                 """
//                 WITH recent AS (
//                   SELECT *
//                   FROM records
//                   WHERE position > 1000
//                 )
//                 /* latest events */
//                 SELECT
//                   r.schema
//                 FROM recent r
//                 INNER JOIN records t ON r.stream_id = t.stream_id
//                 WHERE r.schema = 'urn:com:example:domain:ordering:events:order-placed:v1'
//                 """),
//
//             new("1c1t-skip-heavy", SmallMap,
//                 """
//                 SELECT
//                   schema
//                 FROM records
//                 WHERE schema = 'urn:com:example:domain:inventory:events:stock-reservation-confirmed:v3'
//                 AND data = E'escaped \'quotes\' and \\backslashes\\ in a longer string value here'
//                 /* this is a block comment that contains useful information about the query
//                    and spans multiple lines to exercise the SIMD block comment scanner path */
//                 AND category = $$long dollar-quoted content that exercises the dollar sign scanner path$$
//                 AND [bracketed-identifier-name] IS NOT NULL
//                 """),
//
//             #endregion
//
//             #region 10 columns, 1 table
//
//             new("10c1t-select", MediumMap,
//                 """
//                 SELECT
//                   schema,
//                   revision,
//                   position,
//                   stream_id,
//                   created,
//                   data,
//                   metadata_val,
//                   category,
//                   correlation,
//                   causation
//                 FROM records
//                 """),
//
//             new("10c1t-where", MediumMap,
//                 """
//                 SELECT
//                   schema,
//                   revision,
//                   position
//                 FROM records
//                 WHERE position > 100
//                 AND schema = 'urn:com:example:domain:ordering:events:order-created:v2'
//                 AND revision < 1000
//                 """),
//
//             new("10c1t-cte-join", MediumMap,
//                 """
//                 WITH recent AS (
//                   SELECT
//                     schema,
//                     revision,
//                     position
//                   FROM records
//                   WHERE position > 1000
//                 )
//                 /* latest events */
//                 SELECT
//                   r.schema,
//                   r.revision,
//                   r.position,
//                   r.stream_id
//                 FROM recent r
//                 INNER JOIN records t ON r.stream_id = t.stream_id
//                 WHERE r.schema = 'urn:com:example:domain:ordering:events:order-placed:v1'
//                 AND r.category = 'urn:com:example:domain:ordering'
//                 AND r.correlation = 'urn:correlation:abc-def-ghi-jkl-mno-pqr'
//                 """),
//
//             new("10c1t-skip-heavy", MediumMap,
//                 """
//                 SELECT
//                   schema,
//                   revision,
//                   position,
//                   stream_id,
//                   created,
//                   data,
//                   metadata_val,
//                   category,
//                   correlation,
//                   causation
//                 FROM records
//                 WHERE schema = 'urn:com:example:domain:inventory:events:stock-reservation-confirmed:v3'
//                 AND data = E'escaped \'quotes\' and \\backslashes\\ in a longer string value here'
//                 /* this is a block comment that contains useful information about the query
//                    and spans multiple lines to exercise the SIMD block comment scanner path */
//                 AND category = $$long dollar-quoted content that exercises the dollar sign scanner path$$
//                 AND [bracketed-identifier-name] IS NOT NULL
//                 AND revision > 100 AND position BETWEEN 1000 AND 5000
//                 AND correlation = 'urn:correlation:abc-def-ghi-jkl-mno-pqr' AND causation = 'urn:causation:xyz-uvw-rst-mno-pqr-stu'
//                 """),
//
//             #endregion
//
//             // #region 10 columns, 2 tables
//             //
//             // new("10c2t-select", LargeMap,
//             //     """
//             //     SELECT
//             //       schema,
//             //       revision,
//             //       position,
//             //       stream_id,
//             //       created,
//             //       data,
//             //       metadata_val,
//             //       category,
//             //       correlation,
//             //       causation
//             //     FROM records
//             //     """),
//             //
//             // new("10c2t-where", LargeMap,
//             //     """
//             //     SELECT
//             //       schema,
//             //       revision,
//             //       position
//             //     FROM all_events
//             //     WHERE position > 100
//             //     AND schema = 'urn:com:example:domain:ordering:events:order-created:v2'
//             //     AND revision < 1000
//             //     """),
//             //
//             // new("10c2t-cte-join", LargeMap,
//             //     """
//             //     WITH recent AS (
//             //       SELECT
//             //         schema,
//             //         revision,
//             //         position
//             //       FROM all_events
//             //       WHERE position > 1000
//             //     )
//             //     /* latest events */
//             //     SELECT
//             //       r.schema,
//             //       r.revision,
//             //       r.position,
//             //       r.stream_id
//             //     FROM recent r
//             //     INNER JOIN records t ON r.stream_id = t.stream_id
//             //     WHERE r.schema = 'urn:com:example:domain:ordering:events:order-placed:v1'
//             //     AND r.category = 'urn:com:example:domain:ordering'
//             //     AND r.correlation = 'urn:correlation:abc-def-ghi-jkl-mno-pqr'
//             //     """),
//             //
//             // new("10c2t-skip-heavy", LargeMap,
//             //     """
//             //     SELECT
//             //       schema,
//             //       revision,
//             //       position,
//             //       stream_id,
//             //       created,
//             //       data,
//             //       metadata_val,
//             //       category,
//             //       correlation,
//             //       causation
//             //     FROM records
//             //     WHERE schema = 'urn:com:example:domain:inventory:events:stock-reservation-confirmed:v3'
//             //     AND data = E'escaped \'quotes\' and \\backslashes\\ in a longer string value here'
//             //     /* this is a block comment that contains useful information about the query
//             //        and spans multiple lines to exercise the SIMD block comment scanner path */
//             //     AND category = $$long dollar-quoted content that exercises the dollar sign scanner path$$
//             //     AND [bracketed-identifier-name] IS NOT NULL
//             //     AND revision > 100 AND position BETWEEN 1000 AND 5000
//             //     AND correlation = 'urn:correlation:abc-def-ghi-jkl-mno-pqr' AND causation = 'urn:causation:xyz-uvw-rst-mno-pqr-stu'
//             //     """),
//             //
//             // #endregion
//
//             #region roman-example
//
//             new("roman-example", SmallMap,
//                 """
//                 SELECT "kdb.log" AS "kdb.log" FROM (SELECT "kdb.log" AS "kdb.log" FROM "kdb.log" WHERE "kdb.log"="kdb.log") WHERE "kdb.log"="kdb.log"
//                 """),
//
//             #endregion
//         ];
//     }
//
//     #region Normalizer configurations
//
//     static readonly NormalizerOptions SmallMap = new() {
//         ColumnReplacements = new Dictionary<string, string> {
//             ["schema"] = "event_type",
//         }.ToImmutableDictionary(),
//         TableReplacements = new Dictionary<string, string> {
//             ["records"] = "kdb_log",
//             ["kdb.log"] = "kdb_log",
//         }.ToImmutableDictionary(),
//         ForbiddenTablePrefix = "kdb_"
//     };
//
//     static readonly NormalizerOptions MediumMap = new() {
//         ColumnReplacements = new Dictionary<string, string> {
//             ["schema"]       = "event_type",
//             ["revision"]     = "event_number",
//             ["position"]     = "log_position",
//             ["stream_id"]    = "stream_name",
//             ["created"]      = "created_at",
//             ["data"]         = "payload",
//             ["metadata_val"] = "meta",
//             ["category"]     = "stream_category",
//             ["correlation"]  = "correlation_id",
//             ["causation"]    = "causation_id",
//         }.ToImmutableDictionary(),
//         TableReplacements = new Dictionary<string, string> {
//             ["records"] = "kdb_log",
//             ["kdb.log"] = "kdb_log",
//         }.ToImmutableDictionary(),
//         ForbiddenTablePrefix = "kdb_"
//     };
//
//     static readonly NormalizerOptions LargeMap = new() {
//         ColumnReplacements = new Dictionary<string, string> {
//             ["schema"]       = "event_type",
//             ["revision"]     = "event_number",
//             ["position"]     = "log_position",
//             ["stream_id"]    = "stream_name",
//             ["created"]      = "created_at",
//             ["data"]         = "payload",
//             ["metadata_val"] = "meta",
//             ["category"]     = "stream_category",
//             ["correlation"]  = "correlation_id",
//             ["causation"]    = "causation_id",
//         }.ToImmutableDictionary(),
//         TableReplacements = new Dictionary<string, string> {
//             ["records"]    = "kdb_log",
//             ["all_events"] = "kdb_all",
//             ["kdb.log"]    = "kdb_log",
//         }.ToImmutableDictionary(),
//         ForbiddenTablePrefix = "kdb_"
//     };
//
//     #endregion
//
//     public sealed class NormalizerScenario(string label, NormalizerOptions options, string query) {
//         public NormalizerOptions Options => options;
//         public string            Query   => query;
//
//         public override string ToString() => label;
//     }
// }
//
// [MemoryDiagnoser]
// [Config(typeof(BenchmarkConfig))]
// public class Utf8QueryExpressionNormalizerBenchmarks {
//     LegacyQueryExpressionNormalizer   _legacyNormalizer      = null!;
//     QueryExpressionNormalizer         _newCharNormalizer      = null!;
//     Utf8TranscodingNormalizer         _transcodingNormalizer  = null!;
//     Utf8QueryExpressionNormalizer     _utf8Normalizer         = null!;
//     Utf8QueryExpressionNormalizerV2   _utf8NormalizerV2       = null!;
//     Utf8QueryExpressionNormalizerV3   _utf8NormalizerV3       = null!;
//
//     char[] _charBuffer = null!;
//     byte[] _utf8Input  = null!;
//
//     [ParamsSource(nameof(Scenarios))]
//     public LegacyQueryExpressionNormalizerBenchmarks.NormalizerScenario Scenario { get; set; } = null!;
//
//     // Reuse the same scenario definitions from the existing benchmark class
//     public static IEnumerable<LegacyQueryExpressionNormalizerBenchmarks.NormalizerScenario> Scenarios =>
//         LegacyQueryExpressionNormalizerBenchmarks.Scenarios;
//
//     [GlobalSetup]
//     public void Setup() {
//         _legacyNormalizer      = new LegacyQueryExpressionNormalizer(Scenario.Options);
//         _newCharNormalizer     = new QueryExpressionNormalizer(Scenario.Options);
//         _transcodingNormalizer = new Utf8TranscodingNormalizer(_legacyNormalizer);
//         _utf8Normalizer        = new Utf8QueryExpressionNormalizer(Scenario.Options);
//         _utf8NormalizerV2      = new Utf8QueryExpressionNormalizerV2(Scenario.Options);
//         _utf8NormalizerV3      = new Utf8QueryExpressionNormalizerV3(Scenario.Options);
//
//         _charBuffer = new char[Scenario.Query.Length * 2];
//         _utf8Input  = Encoding.UTF8.GetBytes(Scenario.Query);
//     }
//
//     [Benchmark(Baseline = true)]
//     public bool CharLegacy() {
//         var result = _legacyNormalizer.Normalize(Scenario.Query, _charBuffer);
//         return result.IsValid;
//     }
//
//     [Benchmark]
//     public bool CharNew() {
//         var writer = new BufferWriterSlim<char>(stackalloc char[256]);
//
//         try {
//             _newCharNormalizer.Normalize(Scenario.Query, ref writer);
//             return true;
//         }
//         finally {
//             writer.Dispose();
//         }
//     }
//
//     [Benchmark]
//     public bool Utf8Transcoding() {
//         var writer = new BufferWriterSlim<byte>(stackalloc byte[256]);
//
//         try {
//             _transcodingNormalizer.Normalize(_utf8Input, ref writer);
//             return true;
//         }
//         finally {
//             writer.Dispose();
//         }
//     }
//
//     [Benchmark]
//     public bool Utf8Native() {
//         var writer = new BufferWriterSlim<byte>(stackalloc byte[256]);
//
//         try {
//             _utf8Normalizer.Normalize(_utf8Input, ref writer);
//             return true;
//         }
//         finally {
//             writer.Dispose();
//         }
//     }
//
//     [Benchmark]
//     public bool Utf8NativeV2() {
//         var writer = new BufferWriterSlim<byte>(stackalloc byte[256]);
//
//         try {
//             _utf8NormalizerV2.Normalize(_utf8Input, ref writer);
//             return true;
//         }
//         finally {
//             writer.Dispose();
//         }
//     }
//
//     [Benchmark]
//     public bool Utf8NativeV3() {
//         var writer = new BufferWriterSlim<byte>(stackalloc byte[256]);
//
//         try {
//             _utf8NormalizerV3.Normalize(_utf8Input, ref writer);
//             return true;
//         }
//         finally {
//             writer.Dispose();
//         }
//     }
//
//     [Benchmark]
//     public bool Utf8NativeV4() {
//         var writer = new BufferWriterSlim<byte>(stackalloc byte[_utf8Input.Length]);
//
//         try {
//             _utf8NormalizerV2.Normalize(_utf8Input, ref writer);
//             return true;
//         }
//         finally {
//             writer.Dispose();
//         }
//     }
//
//     public sealed class NormalizerScenario(string label, NormalizerOptions options, string query) {
//         public NormalizerOptions Options => options;
//         public string            Query   => query;
//
//         public override string ToString() => label;
//     }
// }
