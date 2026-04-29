using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace UselessFacts;

/// <summary>
/// Typed client for https://uselessfacts.jsph.pl — a small free API that returns random fun facts.
/// Use <see cref="UselessFactsClient.Create(string)"/> to obtain a preconfigured, AOT/trim-safe instance.
/// </summary>
public interface IUselessFactsApi {
    [Get("/api/v2/facts/random")]
    Task<UselessFact> GetRandomFact(CancellationToken ct = default);

    [Get("/api/v2/facts/today")]
    Task<UselessFact> GetTodaysFact(CancellationToken ct = default);
}

public record UselessFact(
    string Id,
    string Text,
    string Source,
    string SourceUrl,
    string Language,
    string Permalink
);

/// <summary>
/// Source-generated JSON metadata for <see cref="UselessFact"/>. Drives AOT/trim-safe
/// (de)serialization through <see cref="SystemTextJsonContentSerializer"/>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UselessFact))]
public partial class UselessFactsJsonContext : JsonSerializerContext;

/// <summary>
/// Factory for a preconfigured, AOT/trim-safe <see cref="IUselessFactsApi"/>.
/// The concrete interface implementation is produced at compile time by Refit's source generator;
/// JSON (de)serialization is driven by <see cref="UselessFactsJsonContext"/>.
/// </summary>
public static class UselessFactsClient {
    public const string DefaultBaseUrl = "https://uselessfacts.jsph.pl";

    /// <summary>Default Refit settings wired to the source-generated JSON context.</summary>
    internal static RefitSettings DefaultSettings { get; } = new() {
        ContentSerializer = new SystemTextJsonContentSerializer(UselessFactsJsonContext.Default.Options)
    };

    static IRequestBuilder<IUselessFactsApi> DefaultRequestBuilder { get; } =
        RequestBuilder.ForType<IUselessFactsApi>(DefaultSettings);

    public static IUselessFactsApi Create(string baseUrl = DefaultBaseUrl) =>
        RestService.For<IUselessFactsApi>(baseUrl, DefaultSettings);

    public static IUselessFactsApi Create(HttpClient httpClient) =>
        RestService.For(httpClient, DefaultRequestBuilder);
}

/// <summary>
/// Dependency injection extensions for <see cref="IUselessFactsApi"/>.
/// </summary>
public static class UselessFactsServiceCollectionExtensions {
    /// <summary>
    /// Registers <see cref="IUselessFactsApi"/> with <see cref="IHttpClientFactory"/>, pre-wired with
    /// the source-generated JSON serializer and <see cref="UselessFactsClient.DefaultBaseUrl"/>.
    /// Returns <see cref="IHttpClientBuilder"/> so callers can chain handlers, resilience, etc.
    /// </summary>
    public static IHttpClientBuilder AddUselessFactsClient(this IServiceCollection services) =>
        services.AddUselessFactsClient(UselessFactsClient.DefaultBaseUrl);

    /// <summary>
    /// Overload that targets a specific base URL (e.g. staging) while keeping the source-generated
    /// JSON serializer intact.
    /// </summary>
    public static IHttpClientBuilder AddUselessFactsClient(this IServiceCollection services, string baseUrl) =>
        services.AddRefitClient<IUselessFactsApi>(UselessFactsClient.DefaultSettings)
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(baseUrl));
}
