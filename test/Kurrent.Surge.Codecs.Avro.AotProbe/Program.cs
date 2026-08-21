using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Codecs.Avro.AotProbe.Messages;

// Published with PublishAot and run as a process. It is the only check that proves the generated codec
// works under Native AOT: the analyzers can be satisfied by code that still fails at runtime, and the
// failure this probe was written for — Activator.CreateInstance on a generated record — was invisible
// until the binary ran.

var codec = new AvroSchemaCodec();

// Flat record — the original probe case.
var flat         = new OrderPlaced { OrderId = "ORD-1", Amount = 42.5 };
var flatBytes    = codec.Encode(flat);
var flatRestored = (OrderPlaced)codec.Decode(flatBytes, typeof(OrderPlaced));

var flatOk = flatRestored.OrderId == flat.OrderId && flatRestored.Amount == flat.Amount;

Console.WriteLine($"FLAT      registered={AvroCodecRegistry.IsRegistered(typeof(OrderPlaced))} " +
                  $"bytes={flatBytes.Length} roundtrip={(flatOk ? "OK" : "MISMATCH")}");

// Nested record + array + map + enum + nullable union.
var complex = new ComplexOrder {
    OrderId  = "ORD-2",
    Customer = new Customer { Name = "Ripley", Age = 32 },
    Lines    = [
        new OrderLine { Sku = "SKU-A", Quantity = 2 },
        new OrderLine { Sku = "SKU-B", Quantity = 7 }
    ],
    Counters = new Dictionary<string, long> { ["hypersleep"] = 57L, ["decks"] = 3L },
    Status   = OrderStatus.Shipped,
    Note     = "handle with care"
};

var complexBytes    = codec.Encode(complex);
var complexRestored = (ComplexOrder)codec.Decode(complexBytes, typeof(ComplexOrder));

var complexOk =
    complexRestored.OrderId           == complex.OrderId           &&
    complexRestored.Customer.Name     == complex.Customer.Name     &&
    complexRestored.Customer.Age      == complex.Customer.Age      &&
    complexRestored.Lines.Count       == complex.Lines.Count       &&
    complexRestored.Lines[0].Sku      == complex.Lines[0].Sku      &&
    complexRestored.Lines[1].Quantity == complex.Lines[1].Quantity &&
    complexRestored.Counters["hypersleep"] == 57L                  &&
    complexRestored.Counters["decks"]      == 3L                   &&
    complexRestored.Status            == complex.Status            &&
    complexRestored.Note              == complex.Note;

Console.WriteLine($"COMPLEX   registered={AvroCodecRegistry.IsRegistered(typeof(ComplexOrder))} " +
                  $"bytes={complexBytes.Length} roundtrip={(complexOk ? "OK" : "MISMATCH")}");
Console.WriteLine($"          nested={complexRestored.Customer.Name}/{complexRestored.Customer.Age} " +
                  $"array={complexRestored.Lines.Count} map={complexRestored.Counters.Count} " +
                  $"enum={complexRestored.Status} union={complexRestored.Note ?? "<null>"}");

// A null in the union branch must also survive.
var withoutNote  = complex with { Note = null };
var nullBytes    = codec.Encode(withoutNote);
var nullRestored = (ComplexOrder)codec.Decode(nullBytes, typeof(ComplexOrder));

var nullOk = nullRestored.Note is null;

Console.WriteLine($"NULLUNION bytes={nullBytes.Length} note={nullRestored.Note ?? "<null>"} {(nullOk ? "OK" : "MISMATCH")}");

// Multi-branch unions: mixed primitives, nullable multi-branch, and an all-record variant.
var unions = new UnionSample {
    ThreeWay         = 42,
    NullableThreeWay = "text",
    RecordUnion      = new Dog { Bark = "woof" }
};

var unionBytes    = codec.Encode(unions);
var unionRestored = (UnionSample)codec.Decode(unionBytes, typeof(UnionSample));

var unionOk =
    (int)unionRestored.ThreeWay             == 42     &&
    (string?)unionRestored.NullableThreeWay == "text" &&
    unionRestored.RecordUnion is Dog { Bark: "woof" };

Console.WriteLine($"UNIONS    registered={AvroCodecRegistry.IsRegistered(typeof(UnionSample))} " +
                  $"bytes={unionBytes.Length} roundtrip={(unionOk ? "OK" : "MISMATCH")}");
Console.WriteLine($"          threeway={unionRestored.ThreeWay} nullable={unionRestored.NullableThreeWay ?? "<null>"} " +
                  $"variant={unionRestored.RecordUnion.GetType().Name}");

// The null branch of a multi-branch union must also survive.
var unionsNull         = unions with { NullableThreeWay = null, RecordUnion = new Cat { Lives = 9 } };
var unionsNullRestored = (UnionSample)codec.Decode(codec.Encode(unionsNull), typeof(UnionSample));

var unionNullOk = unionsNullRestored.NullableThreeWay is null && unionsNullRestored.RecordUnion is Cat { Lives: 9 };

Console.WriteLine($"UNIONNULL nullable={unionsNullRestored.NullableThreeWay ?? "<null>"} " +
                  $"variant={unionsNullRestored.RecordUnion.GetType().Name} {(unionNullOk ? "OK" : "MISMATCH")}");

// Logical types.
//
// Wire parity against Apache's SpecificDatumWriter is asserted in Kurrent.Surge.Codecs.Avro.Tests, which
// turns KurrentAvroApacheInterop on because that writer is constructed from ISpecificRecord.Schema. This
// probe leaves it off, which is the default: the generated types carry no Schema property at all, so
// nothing here can root Avro.Schema.Parse and pull Newtonsoft.Json into the binary. What this measures
// is what an ordinary consumer links.
var logical = new LogicalSample {
    Id      = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
    Amount  = new AvroDecimal(decimal.Parse("1234.56")),
    Day     = new DateOnly(2026, 8, 19),
    TimeMs  = TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(3_723_000)),
    StampMs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc),
    StampUs = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Utc)
};

var logicalBytes    = codec.Encode(logical);
var logicalRestored = (LogicalSample)codec.Decode(logicalBytes, typeof(LogicalSample));

var logicalOk =
    logicalRestored.Id      == logical.Id      &&
    logicalRestored.Amount  == logical.Amount  &&
    logicalRestored.Day     == logical.Day     &&
    logicalRestored.TimeMs  == logical.TimeMs  &&
    logicalRestored.StampMs == logical.StampMs &&
    logicalRestored.StampUs == logical.StampUs;

Console.WriteLine($"LOGICAL   registered={AvroCodecRegistry.IsRegistered(typeof(LogicalSample))} " +
                  $"bytes={logicalBytes.Length} roundtrip={(logicalOk ? "OK" : "MISMATCH")}");
Console.WriteLine($"          uuid={logicalRestored.Id} decimal={logicalRestored.Amount} date={logicalRestored.Day:yyyy-MM-dd} " +
                  $"time={logicalRestored.TimeMs} stampMs={logicalRestored.StampMs:HH:mm:ss} stampUs={logicalRestored.StampUs:HH:mm:ss}");

// Local timestamps decode through ToLocalTime, which needs timezone data. This binary publishes with
// InvariantGlobalization, so whether that data survives into a native build is a question only a native
// run answers.
var localMoment = new DateTime(2026, 8, 19, 12, 34, 56, DateTimeKind.Unspecified);
var local       = new LocalTimestampSample { LocalMs = localMoment, LocalUs = localMoment };

var localBytes    = codec.Encode(local);
var localRestored = (LocalTimestampSample)codec.Decode(localBytes, typeof(LocalTimestampSample));

var localOk = localRestored.LocalMs == local.LocalMs && localRestored.LocalUs == local.LocalUs;

Console.WriteLine($"LOCALTIME registered={AvroCodecRegistry.IsRegistered(typeof(LocalTimestampSample))} " +
                  $"bytes={localBytes.Length} roundtrip={(localOk ? "OK" : "MISMATCH")}");
Console.WriteLine($"          zone={TimeZoneInfo.Local.Id} ms={localRestored.LocalMs:yyyy-MM-dd HH:mm:ss} " +
                  $"kind={localRestored.LocalMs.Kind} us={localRestored.LocalUs:HH:mm:ss}");

// A decimal backed by a fixed, and two logical types nothing implements. The first exercises BigInteger
// sign extension; the second pair must degrade to their underlying primitives.
var decimals = new DecimalFixedSample {
    Amount   = new AvroDecimal(decimal.Parse("-1234.56", System.Globalization.CultureInfo.InvariantCulture)),
    // Left on the Long default: the codec tests and benchmarks opt into NanoInstant, so this is the one
    // place the erased form is exercised end to end.
    Nanos    = 1_700_000_000_123_456_789L,
    Invented = "erased"
};

var decimalBytes    = codec.Encode(decimals);
var decimalRestored = (DecimalFixedSample)codec.Decode(decimalBytes, typeof(DecimalFixedSample));

var decimalOk =
    decimalRestored.Amount   == decimals.Amount &&
    decimalRestored.Nanos    == decimals.Nanos  &&
    decimalRestored.Invented == decimals.Invented;

Console.WriteLine($"DECFIXED  registered={AvroCodecRegistry.IsRegistered(typeof(DecimalFixedSample))} " +
                  $"bytes={decimalBytes.Length} roundtrip={(decimalOk ? "OK" : "MISMATCH")}");
Console.WriteLine($"          decimal={decimalRestored.Amount} nanos={decimalRestored.Nanos} " +
                  $"erased={decimalRestored.Invented}");

// Schema-driven transcode. Unlike every case above it reads no generated type, so it is the one that
// proves the runtime schema parser links and runs natively — the trimmer drops AvroJsonWriter entirely
// unless something calls it, which is exactly how it stayed unproven until this case existed.
var jsonSchema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schemas", "ComplexOrder.avsc"));
var json       = new AvroJsonWriter(jsonSchema).Write(complexBytes);

var jsonOk =
    json.Contains("\"Ripley\"")           &&
    json.Contains("\"Shipped\"")          &&
    json.Contains("\"hypersleep\"")       &&
    json.Contains("\"handle with care\"");

Console.WriteLine($"AVROJSON  chars={json.Length} transcode={(jsonOk ? "OK" : "MISMATCH")}");
Console.WriteLine($"          {json}");

// Schema conversion, both directions. It reads no generated type either, and it drives JsonNode rather
// than Utf8JsonWriter — a path where the trim analyser is satisfied by code that still needs runtime code
// generation, so the linked binary is the only thing that settles it. The fixpoint is the assertion:
// exporting a reimported schema must reproduce the document it came from.
var surge     = AvroJsonSchema.Export(jsonSchema, new AvroJsonSchemaOptions { Canonical = true });
var reimport  = AvroJsonSchema.Import(surge);
var refixed   = AvroJsonSchema.Export(reimport, new AvroJsonSchemaOptions { Canonical = true });

var schemaOk =
    surge.Contains("\"$schema\"")                    &&
    surge.Contains("\"x-avro\"")                     &&
    reimport.Contains("\"type\": \"record\"")        &&
    refixed == surge;

Console.WriteLine($"AVROSCHEM chars={surge.Length} roundtrip={(schemaOk ? "OK" : "MISMATCH")}");

var allOk = flatOk && complexOk && nullOk && unionOk && unionNullOk && logicalOk && localOk && decimalOk && jsonOk && schemaOk;

Console.WriteLine(allOk ? "RESULT: ALL PASSED" : "RESULT: FAILED");

return allOk ? 0 : 1;
