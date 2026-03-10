using System.Collections.Immutable;
using System.Text;
using BenchmarkDotNet.Attributes;
using DotNext.Buffers;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Benchmarks.Benchmarks;

public sealed record NormalizerScenario(string Label, string Query) {
    public override string ToString() => Label;
}

static class Shared {
    public static readonly NormalizerOptions Options = new () {
        ColumnReplacements = new Dictionary<string, string> {
            ["record_id"]       = "id",
            ["schema"]          = "event_type",
            ["schema_name"]     = "event_type",
            ["revision"]        = "event_number",
            ["stream_revision"] = "event_number",
            ["position"]        = "log_position",
            ["timestamp"]       = "created_at",
            ["create_time"]     = "created_at",
            ["data"]            = "data",
            ["value"]           = "raw_data",
            ["properties"]      = "metadata"
        }.ToImmutableDictionary(),
        TableReplacements = new Dictionary<string, string> {
            ["records"]     = "kdb_log",
            ["kdb.records"] = "kdb_log",
            ["kdb.log"]     = "kdb_log",

            ["categories"]     = "kdb_categories",
            ["kdb.categories"] = "kdb_categories",

            ["connectors"]     = "kdb_connectors",
            ["kdb.connectors"] = "kdb_connectors",

            ["schemas"]     = "kdb_event_types",
            ["kdb.schemas"] = "kdb_event_types",

            ["streams"]     = "kdb_streams",
            ["kdb.streams"] = "kdb_streams"
        }.ToImmutableDictionary(),
        ForbiddenTablePrefix = "kdb_"
    };

    public static NormalizerScenario[] Scenarios() {
        return [
            #region 1 column, 1 table

            new NormalizerScenario("1c1t-select", """SELECT schema FROM records"""),

            new NormalizerScenario(
                "1c1t-where",
                """
                SELECT
                  schema
                FROM records
                WHERE schema = 'urn:com:example:domain:ordering:events:order-created:v2'
                AND position > 100
                """),

            new NormalizerScenario(
                "1c1t-cte-join",
                """
                WITH recent AS (
                  SELECT *
                  FROM records
                  WHERE position > 1000
                )
                SELECT 
                  r.schema 
                FROM recent r 
                INNER JOIN records t ON r.stream_id = t.stream_id
                WHERE r.schema = 'urn:com:example:domain:ordering:events:order-placed:v1'
                """),

            new NormalizerScenario(
                "1c1t-skip-heavy",
                """
                SELECT
                  schema
                FROM records
                WHERE schema = 'urn:com:example:domain:inventory:events:stock-reservation-confirmed:v3'
                AND data = E'escaped \'quotes\' and \\backslashes\\ in a longer string value here'
                AND category = $$long dollar-quoted content that exercises the dollar sign scanner path$$
                AND [bracketed-identifier-name] IS NOT NULL
                AND revision > 100 AND position BETWEEN 1000 AND 5000
                """),

            #endregion

            #region 8 columns, 1 table

            new NormalizerScenario(
                "8c1t-select",
                """
                SELECT 
                    record_id schema, position, stream, category, revision, timestamp, properties
                FROM records
                """),

            new NormalizerScenario(
                "8c1t-where",
                """
                SELECT 
                    record_id schema, position, stream, category, revision, timestamp, properties
                FROM records
                WHERE position > 100
                AND schema = 'urn:com:example:domain:ordering:events:order-created:v2'
                AND revision < 1000
                """),

            new NormalizerScenario(
                "8c1t-cte-join",
                """
                WITH recent AS (
                  SELECT 
                    record_id schema, position, stream, category, revision, timestamp, properties
                  FROM records
                  WHERE position > 1000
                )
                SELECT
                  recent.schema,
                  recent.revision,
                  recent.position,
                  recent.stream
                FROM recent
                INNER JOIN records t ON recent.stream = t.stream
                WHERE recent.schema = 'urn:com:example:domain:ordering:events:order-placed:v1'
                AND recent.category = 'Orders'
                """),

            new NormalizerScenario(
                "8c1t-skip-heavy",
                """
                SELECT 
                  record_id schema, position, stream, category, revision, timestamp, properties
                FROM records
                WHERE schema = 'urn:com:example:domain:inventory:events:stock-reservation-confirmed:v3'
                AND data = E'escaped \'quotes\' and \\backslashes\\ in a longer string value here'
                AND category = $$long dollar-quoted content that exercises the dollar sign scanner path$$
                AND [bracketed-identifier-name] IS NOT NULL
                AND revision > 100 AND position BETWEEN 1000 AND 5000
                """),

            #endregion

            new NormalizerScenario(
                "romans-example",
                """
                SELECT "kdb.log" AS "kdb.log" FROM (SELECT "kdb.log" AS "kdb.log" FROM "kdb.log" WHERE "kdb.log"="kdb.log") WHERE "kdb.log"="kdb.log"
                """
            ),
        ];
    }
}

[MemoryDiagnoser(displayGenColumns: false), Config(typeof(BenchmarkConfig))]
public class QueryExpressionNormalizerBenchmarks {
    LegacyQueryExpressionNormalizer   _legacyNormalizer       = null!;
    QueryExpressionNormalizer         _newCharNormalizer      = null!;
    Utf8TranscodingNormalizer         _transcodingNormalizer  = null!;
    Utf8QueryExpressionNormalizer     _utf8Normalizer         = null!;
    Utf8QueryExpressionNormalizerV2   _utf8NormalizerV2       = null!;
    Utf8QueryExpressionNormalizerV3   _utf8NormalizerV3       = null!;


    byte[] _utf8Input  = null!;

    [ParamsSource(nameof(Scenarios))]
    public NormalizerScenario Scenario { get; set; } = null!;

    public static NormalizerScenario[] Scenarios => Shared.Scenarios();

    [ParamsSource(nameof(BufferSizes))]
    public int BufferSize { get; set; }

    public static int[] BufferSizes => [128, 256, 512, 1024];

    [GlobalSetup]
    public void Setup() {
        _legacyNormalizer      = new LegacyQueryExpressionNormalizer(Shared.Options);
        _newCharNormalizer     = new QueryExpressionNormalizer(Shared.Options);
        _transcodingNormalizer = new Utf8TranscodingNormalizer(_legacyNormalizer);
        _utf8Normalizer        = new Utf8QueryExpressionNormalizer(Shared.Options);
        _utf8NormalizerV2      = new Utf8QueryExpressionNormalizerV2(Shared.Options);
        _utf8NormalizerV3      = new Utf8QueryExpressionNormalizerV3(Shared.Options);

        _utf8Input  = Encoding.UTF8.GetBytes(Scenario.Query);
    }

    [Benchmark(Baseline = true, Description = "Char normalizer (legacy implementation)")]
    public void CharLegacy() {
        var buffer = new char[BufferSize];
        _legacyNormalizer.Normalize(Scenario.Query, buffer);
    }

    [Benchmark(Description = "Char normalizer (new implementation)")]
    public void CharNew() {
        var writer = new BufferWriterSlim<char>(BufferSize);

        try {
            _newCharNormalizer.Normalize(Scenario.Query, ref writer);
        }
        finally {
            writer.Dispose();
        }
    }

    [Benchmark(Description = "UTF8 Transcoding (legacy char normalizer)")]
    public void Utf8Transcoding() {
        var writer = new BufferWriterSlim<byte>(BufferSize);

        try {
            _transcodingNormalizer.Normalize(_utf8Input, ref writer);
        }
        finally {
            writer.Dispose();
        }
    }

    [Benchmark(Description = "UTF8 Native V1")]
    public void Utf8NativeV1() {
        var writer = new BufferWriterSlim<byte>(BufferSize);

        try {
            _utf8Normalizer.Normalize(_utf8Input, ref writer);
        }
        finally {
            writer.Dispose();
        }
    }

    [Benchmark(Description = "UTF8 Native V2")]
    public void Utf8NativeV2() {
        var writer = new BufferWriterSlim<byte>(BufferSize);

        try {
            _utf8NormalizerV2.Normalize(_utf8Input, ref writer);
        }
        finally {
            writer.Dispose();
        }
    }

    [Benchmark(Description = "UTF8 Native V3")]
    public void Utf8NativeV3() {
        var writer = new BufferWriterSlim<byte>(BufferSize);

        try {
            _utf8NormalizerV3.Normalize(_utf8Input, ref writer);
        }
        finally {
            writer.Dispose();
        }
    }
}
