using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

string[] samples = [
    "SELECT * FROM kdb.records",
    "SELECT id FROM kdb.records",
    "SELECT id, timestamp FROM kdb.records",
    "SELECT data.amount AS total FROM kdb.records",
    "SELECT id, data.amount AS total FROM kdb.records WHERE data.amount > 100 LIMIT 50",
    "SELECT * FROM kdb.records WHERE stream = 'orders' AND data.amount >= 100",

    // Negative cases
    "SELECT 1 + 1",
    "SELECT * FROM other.table",
    "INSERT INTO kdb.records VALUES (1)",
    "SELECT * FROM kdb.records UNION SELECT * FROM kdb.records",
];

foreach (var sql in samples) {
    Console.WriteLine($"\n─── {sql}");

    try {
        var query = QueryParser.Parse(sql);
        Console.WriteLine($"    fields : [{string.Join(", ", query.Fields.Select(f => f.Alias is null ? f.Path : $"{f.Path} AS {f.Alias}"))}]");
        Console.WriteLine($"    where  : {query.WhereExpression ?? "<none>"}");
        Console.WriteLine($"    limit  : {query.Limit?.ToString() ?? "<none>"}");
    }
    catch (QueryParseException ex) {
        Console.WriteLine($"    ERROR: {ex.Message}");
    }
    catch (ParserException ex) {
        Console.WriteLine($"    PARSE ERROR: {ex.Message}");
    }
}

sealed record ParsedQuery(
    IReadOnlyList<ProjectionField> Fields,
    string? WhereExpression,
    long? Limit
);

sealed record ProjectionField(string Path, string? Alias) {
    public static readonly ProjectionField All = new("*", null);
}

sealed class QueryParseException(string message) : Exception(message);

static class QueryParser {
    static readonly Parser        Parser  = new();
    static readonly DuckDbDialect Dialect = new();

    public static ParsedQuery Parse(string sql) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var statements = Parser.ParseSql(sql, Dialect);

        if (statements.Count != 1)
            throw new QueryParseException("Only a single statement is supported.");

        if (statements[0] is not Statement.Select selectStmt)
            throw new QueryParseException($"Only SELECT is supported. Got: {statements[0].GetType().Name}.");

        var query = selectStmt.Query;

        if (query.Body is not SetExpression.SelectExpression selectBody)
            throw new QueryParseException("UNION / set operations are not supported.");

        var select = selectBody.Select;

        EnsureSource(select);

        var fields = BuildProjection(select.Projection);
        var where  = select.Selection?.ToSql();
        var limit  = ExtractLimit(query.Limit);

        return new ParsedQuery(fields, where, limit);
    }

    static void EnsureSource(Select select) {
        if (select.From is not { Count: 1 } from)
            throw new QueryParseException("FROM must reference exactly one table (kdb.records).");

        if (from[0].Joins is { Count: > 0 })
            throw new QueryParseException("JOINs are not supported.");

        if (from[0].Relation is not TableFactor.Table table)
            throw new QueryParseException("FROM must reference a table, not a subquery or function.");

        var parts = table.Name.Values;

        if (parts.Count != 2
         || !parts[0].Value.Equals("kdb", StringComparison.OrdinalIgnoreCase)
         || !parts[1].Value.Equals("records", StringComparison.OrdinalIgnoreCase))
            throw new QueryParseException($"FROM must be `kdb.records`. Got: `{string.Join('.', parts.Select(p => p.Value))}`.");
    }

    static IReadOnlyList<ProjectionField> BuildProjection(Sequence<SelectItem> items) {
        var fields = new List<ProjectionField>(items.Count);

        foreach (var item in items) {
            ProjectionField field = item switch {
                SelectItem.Wildcard => ProjectionField.All,
                SelectItem.UnnamedExpression { Expression  : Expression.Identifier id } => new(id.Ident.Value, null),
                SelectItem.UnnamedExpression { Expression  : Expression.CompoundIdentifier ci } => new(string.Join('.', ci.Idents.Select(i => i.Value)), null),
                SelectItem.ExpressionWithAlias { Expression: Expression.Identifier id, Alias        : var a } => new(id.Ident.Value, a.Value),
                SelectItem.ExpressionWithAlias { Expression: Expression.CompoundIdentifier ci, Alias: var a } => new(string.Join('.', ci.Idents.Select(i => i.Value)), a.Value),
                _ => throw new QueryParseException($"Unsupported projection item: {item.GetType().Name}")
            };

            fields.Add(field);
        }

        return fields;
    }

    static long? ExtractLimit(Expression? limit) {
        if (limit is null)
            return null;

        if (limit is Expression.LiteralValue { Value: Value.Number num }
         && long.TryParse(num.Value, out var n))
            return n;

        throw new QueryParseException($"LIMIT must be an integer literal. Got: {limit.GetType().Name}.");
    }
}