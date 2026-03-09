namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

enum KeywordKind {
    /// <summary>
    /// Not a recognized keyword. The identifier is subject to replacement based on the current
    /// scanner state (column replacement in Default, table replacement in ExpectingTable).
    /// </summary>
    None,

    /// <summary>
    /// FROM, JOIN, INTO, UPDATE, TABLE, PIVOT, UNPIVOT. Transitions the scanner to
    /// <see cref="ScannerState.ExpectingTable"/>.
    /// </summary>
    TableIntroducer,

    /// <summary>
    /// LEFT, RIGHT, INNER, OUTER, FULL, CROSS, NATURAL, LATERAL, SEMI, ANTI, ASOF, POSITIONAL.
    /// Transparent in table context; the scanner stays in ExpectingTable waiting for the actual table
    /// name. In Default context, treated as a regular keyword (copied verbatim).
    /// </summary>
    JoinModifier,

    /// <summary>
    /// SELECT, WHERE, ON, SET, ORDER, GROUP, HAVING, LIMIT, UNION, VALUES, RETURNING, QUALIFY,
    /// WINDOW, USING, OFFSET, EXCEPT, INTERSECT, FILTER, SAMPLE. Resets the scanner to
    /// <see cref="ScannerState.Default"/> (column context).
    /// </summary>
    ContextReset,

    /// <summary>
    /// AS keyword. Transitions to <see cref="ScannerState.ExpectingAlias"/> in column context
    /// or <see cref="ScannerState.ExpectingTableAlias"/> in table context.
    /// </summary>
    As,

    /// <summary>
    /// WITH keyword. Transitions to <see cref="ScannerState.ExpectingCTE"/>.
    /// </summary>
    With,

    /// <summary>
    /// LAMBDA keyword. Transitions to <see cref="ScannerState.AfterLambda"/> where identifiers
    /// are treated as parameter names (verbatim) until the <c>:</c> delimiter.
    /// </summary>
    Lambda,

    /// <summary>
    /// RECURSIVE keyword. Transparent inside <see cref="ScannerState.ExpectingCTE"/>; the scanner
    /// stays in that state. In any other context, treated as a regular identifier.
    /// </summary>
    Recursive
}

#endif
