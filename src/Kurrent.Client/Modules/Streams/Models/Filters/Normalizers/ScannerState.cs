namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

/// <summary>
/// Tracks the scanner position within the query grammar to determine how the next
/// identifier should be treated: column replacement, table replacement, or verbatim copy.
/// </summary>
enum ScannerState {
    /// <summary>
    /// Column context. Bare identifiers are looked up in the column replacement map.
    /// </summary>
    Default,

    /// <summary>
    /// After a table introducer (FROM, JOIN, INTO, UPDATE, TABLE, PIVOT, UNPIVOT). Next identifier
    /// is looked up in the table replacement map and checked against the forbidden prefix.
    /// </summary>
    ExpectingTable,

    /// <summary>
    /// After consuming a table name. Watching for dot (schema.table), comma (multi-table list),
    /// AS (explicit alias), another table introducer, or a context-resetting keyword.
    /// </summary>
    AfterTable,

    /// <summary>
    /// After a dot in table position (e.g. <c>schema.table</c>). The preceding identifier was the
    /// schema qualifier; the next identifier is the actual table name and gets table replacement.
    /// </summary>
    ExpectingQualifiedTable,

    /// <summary>
    /// After AS in column context. Next identifier is an alias; copied verbatim without
    /// replacement. Transitions back to Default.
    /// </summary>
    ExpectingAlias,

    /// <summary>
    /// After AS in table context (e.g. <c>FROM x AS alias</c>). Next identifier is a table alias;
    /// copied verbatim. Transitions back to AfterTable.
    /// </summary>
    ExpectingTableAlias,

    /// <summary>
    /// After WITH keyword. Next identifier is a CTE name; copied verbatim without replacement.
    /// RECURSIVE is transparent and keeps the state in ExpectingCTE.
    /// </summary>
    ExpectingCTE,

    /// <summary>
    /// After a CTE name has been consumed. Watching for comma (another CTE definition) or any
    /// other token which transitions to Default for the CTE body.
    /// </summary>
    AfterCTE,

    /// <summary>
    /// After the LAMBDA keyword. All following identifiers until <c>:</c> are lambda parameter
    /// names; copied verbatim. The colon ends the parameter list and transitions back to Default.
    /// </summary>
    AfterLambda,

    /// <summary>
    /// After the <c>::</c> cast operator. Next identifier is a type name (e.g. <c>::int</c>);
    /// copied verbatim without replacement. Transitions back to Default.
    /// </summary>
    AfterCast
}

#endif
