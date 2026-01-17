using System.Linq.Expressions;
using System.Numerics;

namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Fluent builder for SQL-like table state operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides a LINQ-style method chain API for querying and modifying tabular data.
/// Data is stored as JSON in the underlying database.
/// </para>
/// <para>
/// <b>Schema setup</b> (typically in OnInitialize):
/// <see cref="WithIndex{TField}"/>, <see cref="EnsureCreatedAsync"/>
/// </para>
/// <para>
/// <b>Query building</b> (pipeline composition):
/// <see cref="Where"/>, <see cref="Select{TResult}"/>, <see cref="OrderBy{TField}"/>,
/// <see cref="OrderByDescending{TField}"/>, <see cref="Take"/>, <see cref="Skip"/>
/// </para>
/// <para>
/// <b>Terminal operations</b> (execute the query):
/// Read: <see cref="ToArrayAsync"/>, <see cref="FirstOrDefaultAsync"/>, etc.
/// Aggregates: <see cref="CountAsync"/>, <see cref="SumAsync{TField}"/>, etc.
/// Write: <see cref="InsertAsync"/>, <see cref="UpdateAsync"/>, <see cref="UpsertAsync(T,CancellationToken)"/>, <see cref="DeleteAsync"/>
/// </para>
/// <para>
/// <b>Primary key convention</b> for upsert operations:
/// <list type="number">
/// <item>Property named "Id"</item>
/// <item>Property named "{TypeName}Id" (e.g., OrderId for Order)</item>
/// <item>Property named "{TypeName}_Id" (e.g., Order_Id)</item>
/// <item>Explicit via <see cref="UpsertAsync{TId}(T,Expression{Func{T,TId}},CancellationToken)"/></item>
/// </list>
/// </para>
/// </remarks>
/// <typeparam name="T">The entity type. Must be a reference type.</typeparam>
/// <example>
/// <code>
/// // Schema setup in OnInitialize
/// await ctx.State
///     .Table&lt;Order&gt;("orders")
///     .WithIndex(o =&gt; o.CustomerId)
///     .EnsureCreatedAsync();
///
/// // Query with filter, order, and limit
/// var topOrders = await ctx.State
///     .Table&lt;Order&gt;("orders")
///     .Where(o =&gt; o.CustomerId == customerId)
///     .OrderByDescending(o =&gt; o.Amount)
///     .Take(10)
///     .ToArrayAsync();
///
/// // Aggregates
/// var totalAmount = await ctx.State
///     .Table&lt;Order&gt;("orders")
///     .Where(o =&gt; o.Status == "completed")
///     .SumAsync(o =&gt; o.Amount);
/// </code>
/// </example>
[PublicAPI]
public interface ITableStateBuilder<T> where T : class {
    /// <summary>
    /// Gets the state key for this table.
    /// </summary>
    StateKey Key { get; }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Schema setup (for OnInitialize)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Specifies a field to index for faster queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Indexes improve query performance for fields used in <see cref="Where"/> predicates.
    /// Call this method multiple times for multiple indexes.
    /// </para>
    /// </remarks>
    /// <typeparam name="TField">The type of the field to index.</typeparam>
    /// <param name="field">Expression identifying the field to index.</param>
    /// <returns>This builder for method chaining.</returns>
    ITableStateBuilder<T> WithIndex<TField>(Expression<Func<T, TField>> field);

    /// <summary>
    /// Ensures the table and its indexes are created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typically called once in the processor's <c>OnInitialize</c> method.
    /// Safe to call multiple times; existing tables and indexes are not recreated.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask EnsureCreatedAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Query building (LINQ-style, pipeline composition)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Filters the results based on the specified predicate.
    /// </summary>
    /// <param name="predicate">An expression that returns true for entities to include.</param>
    /// <returns>This builder for method chaining.</returns>
    ITableStateBuilder<T> Where(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Projects each entity into a new form.
    /// </summary>
    /// <typeparam name="TResult">The type of the projected entity.</typeparam>
    /// <param name="selector">A projection expression.</param>
    /// <returns>A new builder for the projected type.</returns>
    ITableStateBuilder<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class;

    /// <summary>
    /// Orders the results by the specified field in ascending order.
    /// </summary>
    /// <typeparam name="TField">The type of the field to order by.</typeparam>
    /// <param name="field">Expression identifying the field to order by.</param>
    /// <returns>This builder for method chaining.</returns>
    ITableStateBuilder<T> OrderBy<TField>(Expression<Func<T, TField>> field);

    /// <summary>
    /// Orders the results by the specified field in descending order.
    /// </summary>
    /// <typeparam name="TField">The type of the field to order by.</typeparam>
    /// <param name="field">Expression identifying the field to order by.</param>
    /// <returns>This builder for method chaining.</returns>
    ITableStateBuilder<T> OrderByDescending<TField>(Expression<Func<T, TField>> field);

    /// <summary>
    /// Limits the number of results returned.
    /// </summary>
    /// <param name="count">The maximum number of entities to return.</param>
    /// <returns>This builder for method chaining.</returns>
    ITableStateBuilder<T> Take(int count);

    /// <summary>
    /// Skips the specified number of results.
    /// </summary>
    /// <param name="count">The number of entities to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    ITableStateBuilder<T> Skip(int count);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Read
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Asynchronously enumerates all matching entities.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An async enumerable of entities.</returns>
    IAsyncEnumerable<T> ToAsyncEnumerable(CancellationToken ct = default);

    /// <summary>
    /// Returns all matching entities as an array.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An array of matching entities.</returns>
    ValueTask<T[]> ToArrayAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all matching entities as a list.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A list of matching entities.</returns>
    ValueTask<List<T>> ToListAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the first matching entity or <c>null</c> if none match.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The first matching entity, or <c>null</c>.</returns>
    ValueTask<T?> FirstOrDefaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the first matching entity, throwing if none match.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The first matching entity.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no entities match.</exception>
    ValueTask<T> FirstAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the only matching entity or <c>null</c> if none match.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The single matching entity, or <c>null</c>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when more than one entity matches.</exception>
    ValueTask<T?> SingleOrDefaultAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Aggregates
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the count of matching entities.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of matching entities.</returns>
    ValueTask<long> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Determines whether any entities match the current query.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> if any entities match; otherwise, <c>false</c>.</returns>
    ValueTask<bool> AnyAsync(CancellationToken ct = default);

    /// <summary>
    /// Computes the sum of a numeric field.
    /// </summary>
    /// <typeparam name="TField">The numeric type of the field.</typeparam>
    /// <param name="field">Expression identifying the field to sum.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The sum of the field values.</returns>
    ValueTask<TField> SumAsync<TField>(Expression<Func<T, TField>> field, CancellationToken ct = default)
        where TField : INumber<TField>;

    /// <summary>
    /// Finds the maximum value of a field.
    /// </summary>
    /// <typeparam name="TField">The type of the field.</typeparam>
    /// <param name="field">Expression identifying the field.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The maximum value, or <c>null</c> if no entities match.</returns>
    ValueTask<TField?> MaxAsync<TField>(Expression<Func<T, TField>> field, CancellationToken ct = default);

    /// <summary>
    /// Finds the minimum value of a field.
    /// </summary>
    /// <typeparam name="TField">The type of the field.</typeparam>
    /// <param name="field">Expression identifying the field.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The minimum value, or <c>null</c> if no entities match.</returns>
    ValueTask<TField?> MinAsync<TField>(Expression<Func<T, TField>> field, CancellationToken ct = default);

    /// <summary>
    /// Computes the average of a numeric field.
    /// </summary>
    /// <typeparam name="TField">The numeric type of the field.</typeparam>
    /// <param name="field">Expression identifying the field to average.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The average of the field values.</returns>
    ValueTask<double> AverageAsync<TField>(Expression<Func<T, TField>> field, CancellationToken ct = default)
        where TField : INumber<TField>;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Write (return affected counts)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inserts a new entity.
    /// </summary>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entities inserted (1 on success, 0 on failure).</returns>
    ValueTask<int> InsertAsync(T entity, CancellationToken ct = default);

    /// <summary>
    /// Inserts multiple entities.
    /// </summary>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entities inserted.</returns>
    ValueTask<int> InsertAllAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>
    /// Updates all matching entities using the specified modifier.
    /// </summary>
    /// <param name="modifier">An action that modifies each entity.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entities updated.</returns>
    ValueTask<int> UpdateAsync(Action<T> modifier, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an entity using the primary key convention.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the primary key convention to determine whether to insert or update:
    /// <list type="number">
    /// <item>Property named "Id"</item>
    /// <item>Property named "{TypeName}Id" (e.g., OrderId for Order)</item>
    /// <item>Property named "{TypeName}_Id" (e.g., Order_Id)</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="entity">The entity to upsert.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Result indicating whether the entity was inserted or updated.</returns>
    ValueTask<UpsertResult> UpsertAsync(T entity, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates an entity using an explicit key selector.
    /// </summary>
    /// <typeparam name="TId">The type of the key.</typeparam>
    /// <param name="entity">The entity to upsert.</param>
    /// <param name="keySelector">Expression identifying the key property.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Result indicating whether the entity was inserted or updated.</returns>
    ValueTask<UpsertResult> UpsertAsync<TId>(T entity, Expression<Func<T, TId>> keySelector, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Terminal: Delete (return affected counts)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deletes all matching entities.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entities deleted.</returns>
    ValueTask<int> DeleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes all entities from the table.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of entities removed.</returns>
    ValueTask<int> TruncateAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Raw SQL (escape hatch)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Executes a raw SQL query and returns the results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this as an escape hatch for complex queries not supported by the fluent API.
    /// The SQL should be compatible with the underlying database (DuckDB).
    /// </para>
    /// </remarks>
    /// <typeparam name="TResult">The type to deserialize results into.</typeparam>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="parameters">Optional parameters for the query.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An array of query results.</returns>
    ValueTask<TResult[]> QueryAsync<TResult>(string sql, object? parameters = null, CancellationToken ct = default)
        where TResult : class;

    /// <summary>
    /// Executes a raw SQL command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this as an escape hatch for complex operations not supported by the fluent API.
    /// The SQL should be compatible with the underlying database (DuckDB).
    /// </para>
    /// </remarks>
    /// <param name="sql">The SQL command to execute.</param>
    /// <param name="parameters">Optional parameters for the command.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of rows affected.</returns>
    ValueTask<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default);
}
