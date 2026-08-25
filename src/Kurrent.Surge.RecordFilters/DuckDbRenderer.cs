using System.Globalization;
using System.Text;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Renders an expression tree as a SQL predicate, resolving every column reference through the same
/// schema the evaluator reads. Aliases collapse to their canonical column name and keyed access is
/// written in arrow notation, so the text sent to a server means what the .NET evaluation meant.
/// </summary>
static class DuckDbRenderer {
    public static string Render<T>(IFilterExpression expression, FilterSchema<T> schema) {
        var builder = new StringBuilder();
        Write(expression, schema, builder);
        return builder.ToString();
    }

    static void Write<T>(IFilterExpression expression, FilterSchema<T> schema, StringBuilder builder) {
        switch (expression) {
            case AndExpression e:
                WriteBinary(e.Left, e.Right, "AND", schema, builder);
                return;

            case OrExpression e:
                WriteBinary(e.Left, e.Right, "OR", schema, builder);
                return;

            case NotExpression e:
                builder.Append("NOT (");
                Write(e.Inner, schema, builder);
                builder.Append(')');
                return;

            case ComparisonExpression e:
                builder.Append(Column(e.Property, schema)).Append(' ').Append(Operator()).Append(' ');
                WriteLiteral(e.Value, builder);

                string Operator() =>
                    e.Op switch {
                        ComparisonOperator.Equal              => "=",
                        ComparisonOperator.NotEqual           => "!=",
                        ComparisonOperator.GreaterThan        => ">",
                        ComparisonOperator.LessThan           => "<",
                        ComparisonOperator.GreaterThanOrEqual => ">=",
                        ComparisonOperator.LessThanOrEqual    => "<=",
                        _                                     => throw new NotSupportedException($"Unhandled operator '{e.Op}'.")
                    };

                return;

            case IsNullExpression e:
                builder.Append(Column(e.Property, schema)).Append(e.Negated ? " IS NOT NULL" : " IS NULL");
                return;

            case LikeExpression e:
                builder.Append(Column(e.Property, schema));
                builder.Append(e.Negated ? " NOT " : " ").Append(e.CaseInsensitive ? "ILIKE " : "LIKE ");
                WriteLiteral(new StringFilterValue(e.Pattern), builder);
                return;

            case InExpression e:
                builder.Append(Column(e.Property, schema)).Append(e.Negated ? " NOT IN (" : " IN (");

                for (var i = 0; i < e.Values.Count; i++) {
                    if (i > 0) builder.Append(", ");
                    WriteLiteral(e.Values[i], builder);
                }

                builder.Append(')');
                return;

            case BetweenExpression e:
                builder.Append(Column(e.Property, schema)).Append(e.Negated ? " NOT BETWEEN " : " BETWEEN ");
                WriteLiteral(e.Lower, builder);
                builder.Append(" AND ");
                WriteLiteral(e.Upper, builder);
                return;

            default:
                throw new NotSupportedException($"Unhandled expression node '{expression.GetType().Name}'.");
        }
    }

    static void WriteBinary<T>(IFilterExpression left, IFilterExpression right, string op, FilterSchema<T> schema, StringBuilder builder) {
        builder.Append('(');
        Write(left, schema, builder);
        builder.Append(") ").Append(op).Append(" (");
        Write(right, schema, builder);
        builder.Append(')');
    }

    static string Column<T>(PropertyAccess property, FilterSchema<T> schema) {
        var column = schema.Find(property.PropertyName, property.Key, out var key)
                  ?? throw new InvalidOperationException(
                         $"Cannot render '{property.PropertyName}' as SQL: no column with that name is declared in FilterSchema<{typeof(T).Name}>.");

        var text = Access();

        // Transforms are held innermost first, so wrapping in order rebuilds what was written.
        foreach (var transform in property.Transforms) {
            text = transform switch {
                CastTransform cast     => $"{(cast.Strict ? "CAST" : "TRY_CAST")}({text} AS {cast.TargetType})",
                FunctionTransform call => Call(call, text),
                _                      => throw new NotSupportedException($"Unhandled transform '{transform.GetType().Name}'.")
            };
        }

        return text;

        string Access() {
            if (key is null) return column.Name;

            // The key already carries its own form — a JSONPath when a dotted name descended into a
            // JSON column, a plain member name otherwise. Adding a prefix here would double it.
            return column.Kind switch {
                FilterColumnKind.Scalar => column.Name,
                FilterColumnKind.Map or
                FilterColumnKind.Json   => $"{column.Name}->>'{EscapeText(key)}'",
                _                       => throw new NotSupportedException($"Unhandled column kind '{column.Kind}'.")
            };
        }

        static string Call(FunctionTransform call, string operand) {
            var builder = new StringBuilder(call.Name).Append('(').Append(operand);

            foreach (var argument in call.Arguments) {
                builder.Append(", ");
                WriteLiteral(argument, builder);
            }

            return builder.Append(')').ToString();
        }
    }

    static StringBuilder WriteLiteral(FilterValue value, StringBuilder builder) =>
        value switch{
            StringFilterValue s   => builder.Append('\'').Append(EscapeText(s.Value)).Append('\''),
            NumberFilterValue n   => builder.Append(n.Value.ToString(CultureInfo.InvariantCulture)),
            BooleanFilterValue b  => builder.Append(b.Value ? "TRUE" : "FALSE"),
            NullFilterValue       => builder.Append("NULL"),
            // Bytes go back as the call that produced them; SQL has no portable blob literal.
            BlobFilterValue blob  => builder.Append("from_hex('").Append(Convert.ToHexString(blob.Value)).Append("')"),
            DateTimeFilterValue d => builder.Append(d.TypeName).Append(" '").Append(d.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\''),
            _                     => throw new NotSupportedException($"Unhandled literal '{value.GetType().Name}'.")
        };

    // SQL escapes a quote by doubling it. The tokenizer accepts backslash escapes on input, so a
    // parsed value can hold a bare quote that must be re-escaped on the way out.
    static string EscapeText(string value) => value.Replace("'", "''");
}
