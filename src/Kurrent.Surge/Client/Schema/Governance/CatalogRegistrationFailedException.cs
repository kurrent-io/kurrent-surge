using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Governance;

/// <summary>
/// Exception thrown when the startup catalog registration walk fails.
/// Carries every catalog entry that failed, not only the first.
/// </summary>
public sealed class CatalogRegistrationFailedException(IReadOnlyList<CatalogRegistrationFailure> failures)
	: KurrentException(ErrorCode.FromType<CatalogRegistrationFailedException>(), ErrorMessage(failures), failures[0].Cause) {
	/// <summary>
	/// Every catalog entry that failed registration or verification, with its cause.
	/// </summary>
	public IReadOnlyList<CatalogRegistrationFailure> Failures { get; } = failures;

	static string ErrorMessage(IReadOnlyList<CatalogRegistrationFailure> failures) =>
		$"Catalog registration failed for {failures.Count} schema(s):{Environment.NewLine}"
	  + string.Join(Environment.NewLine, failures.Select(failure => $" - '{failure.Schema}' ({failure.MessageType.FullName}): {failure.Cause.Message}"));
}

/// <summary>
/// A single catalog entry that failed registration or verification.
/// </summary>
public sealed record CatalogRegistrationFailure(SchemaName Schema, Type MessageType, Exception Cause);
