namespace Kurrent.Client.Integration.Tests.Processors;

/// <summary>
/// Test extension methods for assertions.
/// </summary>
public static class TestExtensions {
	/// <summary>
	/// Asserts that a string value is a valid GUID.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="customMessage">Optional custom message for the assertion failure.</param>
	public static void ShouldBeGuid(this string value, string? customMessage = null) {
		var message = customMessage ?? $"Expected GUID but got: {value}";
		Guid.TryParse(value, out _).ShouldBeTrue(message);
	}

	/// <summary>
	/// Asserts that a string value is NOT empty and is a valid GUID.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="customMessage">Optional custom message for the assertion failure.</param>
	public static void ShouldNotBeEmptyAndShouldBeGuid(this string value, string? customMessage = null) {
		value.ShouldNotBeNullOrEmpty();
		value.ShouldBeGuid(customMessage);
	}
}
