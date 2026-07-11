using Kurrent.Surge.Client.Queries;

var passed = 0;
var failed = 0;

void Ok(string sql, Action<ParsedQuery> assert) {
    try {
        var q = QuerySqlParser.Parse(sql);
        assert(q);
        Console.WriteLine($"  PASS  {sql}");
        passed++;
    }
    catch (Exception ex) {
        Console.WriteLine($"  FAIL  {sql}");
        Console.WriteLine($"        → {ex.GetType().Name}: {ex.Message}");
        failed++;
    }
}

void Err(string sql, string expectedMessageFragment) {
    try {
        QuerySqlParser.Parse(sql);
        Console.WriteLine($"  FAIL  {sql}");
        Console.WriteLine($"        → expected error containing `{expectedMessageFragment}`, got success");
        failed++;
    }
    catch (QueryParseException ex) {
        if (ex.Message.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase)) {
            Console.WriteLine($"  PASS  (err) {sql}");
            passed++;
        }
        else {
            Console.WriteLine($"  FAIL  (err) {sql}");
            Console.WriteLine($"        → expected `{expectedMessageFragment}`, got `{ex.Message}`");
            failed++;
        }
    }
    catch (Exception ex) {
        Console.WriteLine($"  FAIL  (err) {sql}");
        Console.WriteLine($"        → wrong exception {ex.GetType().Name}: {ex.Message}");
        failed++;
    }
}

Console.WriteLine("─── positive cases ───────────────────────────────────────────");

Ok("SELECT stream FROM kdb.records", q => {
    Assert(q.Fields.Count == 1 && q.Fields[0].Path == "stream" && q.Fields[0].Alias is null);
    Assert(q.Where is null && q.Limit is null);
});

Ok("SELECT stream, schema_name, ts FROM kdb.records", q => {
    Assert(q.Fields.Count == 3);
    Assert(q.Fields[0].Path == "stream");
    Assert(q.Fields[1].Path == "schema_name");
    Assert(q.Fields[2].Path == "ts");
});

Ok("SELECT data.amount FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "data.amount");
});

Ok("SELECT data.customer.country FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "data.customer.country");
});

Ok("SELECT metadata['region'] FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "metadata['region']");
});

Ok("SELECT properties[\"userId\"] FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "properties[\"userId\"]");
});

Ok("SELECT properties->>'amount' FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "properties->>'amount'");
});

Ok("SELECT data.amount AS total FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "data.amount" && q.Fields[0].Alias == "total");
});

Ok("SELECT stream AS s, data.amount AS amt FROM kdb.records", q => {
    Assert(q.Fields[0] is { Path: "stream",      Alias: "s"   });
    Assert(q.Fields[1] is { Path: "data.amount", Alias: "amt" });
});

Ok("SELECT stream FROM kdb.records WHERE stream = 'orders'", q => {
    Assert(q.Where is not null);
    Assert(q.Fields[0].Path == "stream");
});

Ok("SELECT stream FROM kdb.records LIMIT 100", q => {
    Assert(q.Limit == 100 && q.Where is null);
});

Ok("SELECT stream, data.amount FROM kdb.records WHERE data.amount > 100 LIMIT 50", q => {
    Assert(q.Fields.Count == 2);
    Assert(q.Where is not null);
    Assert(q.Limit == 50);
});

Ok("select stream from kdb.records", q => {
    Assert(q.Fields[0].Path == "stream");
});

Ok("Select Stream As S From Kdb.Records Where stream = 'orders' Limit 10", q => {
    Assert(q.Fields[0] is { Path: "Stream", Alias: "S" });
    Assert(q.Where is not null && q.Limit == 10);
});

Ok("SELECT metadata['region'], metadata['tenant'] FROM kdb.records WHERE metadata['region'] = 'us-east'", q => {
    Assert(q.Fields.Count == 2);
    Assert(q.Where is not null);
});

// WHERE expression containing the word LIMIT inside a string literal — must not be treated as LIMIT clause
Ok("SELECT stream FROM kdb.records WHERE stream = 'LIMIT'", q => {
    Assert(q.Where is not null);
    Assert(q.Limit is null);
});

// Arrow path combined with alias
Ok("SELECT properties->>'userId' AS user FROM kdb.records", q => {
    Assert(q.Fields[0] is { Path: "properties->>'userId'", Alias: "user" });
});

// Path followed by whitespace and comma
Ok("SELECT  stream   ,   ts  FROM  kdb.records", q => {
    Assert(q.Fields.Count == 2);
});

// Complex WHERE
Ok("SELECT stream FROM kdb.records WHERE (stream = 'a' OR stream = 'b') AND data.amount > 100", q => {
    Assert(q.Where is not null);
});

// WHERE + LIMIT with nested parens
Ok("SELECT stream FROM kdb.records WHERE (stream = 'a' AND (data.amount BETWEEN 1 AND 100)) LIMIT 5", q => {
    Assert(q.Where is not null && q.Limit == 5);
});

// ─── path-form combinations ───
Ok("SELECT data.customer['country'] FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "data.customer['country']");
});

Ok("SELECT metadata['region'].city FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "metadata['region'].city");
});

Ok("SELECT data.customer->>'country' FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "data.customer->>'country'");
});

Ok("SELECT metadata['a']->>'b' FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "metadata['a']->>'b'");
});

Ok("SELECT data.a['b'].c['d'] FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "data.a['b'].c['d']");
});

Ok("SELECT data.customer['country'] AS country FROM kdb.records WHERE data.amount > 100", q => {
    Assert(q.Fields[0] is { Path: "data.customer['country']", Alias: "country" });
    Assert(q.Where is not null);
});

// bracket key containing dot — must be preserved
Ok("SELECT metadata['order.total'] FROM kdb.records", q => {
    Assert(q.Fields[0].Path == "metadata['order.total']");
});

// bracket key containing comma — must not split the projection list
Ok("SELECT metadata['a,b'], stream FROM kdb.records", q => {
    Assert(q.Fields.Count == 2);
    Assert(q.Fields[0].Path == "metadata['a,b']");
    Assert(q.Fields[1].Path == "stream");
});

Console.WriteLine();
Console.WriteLine("─── negative cases ───────────────────────────────────────────");

Err("",                                          "empty");
Err("   ",                                       "empty");
Err("SELECT * FROM kdb.records",                 "projection");
Err("SELECT FROM kdb.records",                   "projection");
Err("SELECT stream",                             "FROM");
Err("SELECT stream FROM other.table",            "kdb.records");
Err("SELECT stream FROM kdb",                    "kdb.records");
Err("SELECT stream FROM kdb.records WHERE",      "WHERE");
Err("SELECT stream FROM kdb.records LIMIT",      "integer");
Err("SELECT stream FROM kdb.records LIMIT abc",  "integer");
Err("SELECT stream FROM kdb.records JUNK",       "trailing");
Err("SELECT stream, FROM kdb.records",           "projection");

Console.WriteLine();
Console.WriteLine($"─── {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static void Assert(bool cond) {
    if (!cond) throw new Exception("assertion failed");
}
