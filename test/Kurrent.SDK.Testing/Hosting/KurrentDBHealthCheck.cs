using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kurrent.SDK.Testing.Hosting;

public class KurrentDBHealthCheck(string connectionString) : IHealthCheck {
    static readonly HttpClient Http = CreateHttpClient();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) {
        try {
            var uri       = new Uri(connectionString);
            var tls       = connectionString.Contains("tls=true", StringComparison.OrdinalIgnoreCase);
            var scheme    = tls ? "https" : "http";
            var healthUrl = $"{scheme}://{uri.Host}:{uri.Port}/health/live";

            var response = await Http.GetAsync(healthUrl, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"KurrentDB health check returned {response.StatusCode}."
                );
        }
        catch (Exception ex) {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }

    static HttpClient CreateHttpClient() {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }
}
