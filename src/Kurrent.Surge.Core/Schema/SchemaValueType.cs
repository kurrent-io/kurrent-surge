// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Surge.Schema;

/// <summary>
/// Identifies the JSON value types that a schema constrains an instance to.
/// </summary>
/// <remarks>
/// Values combine, because the JSON Schema <c>type</c> keyword accepts an array of names.
/// </remarks>
[Flags]
public enum SchemaValueType {
    /// <summary>No type constraint.</summary>
    None = 0,

    /// <summary>An object.</summary>
    Object = 1 << 0,

    /// <summary>An array.</summary>
    Array = 1 << 1,

    /// <summary>A string.</summary>
    String = 1 << 2,

    /// <summary>A number.</summary>
    Number = 1 << 3,

    /// <summary>An integer.</summary>
    Integer = 1 << 4,

    /// <summary>A boolean.</summary>
    Boolean = 1 << 5,

    /// <summary>A null.</summary>
    Null = 1 << 6
}
