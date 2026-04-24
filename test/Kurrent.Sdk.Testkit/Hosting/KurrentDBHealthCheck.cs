using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kurrent.Sdk.Testkit.Hosting;

public class KurrentDBHealthCheck(KurrentDBResource resource) : IHealthCheck {
    static readonly HttpClient Http = CreateHttpClient();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) {
        try {
            var scheme   = resource.Secure ? "https" : "http";
            var endpoint = resource.PrimaryEndpoint;
            var url      = new Uri($"{scheme}://{endpoint.Host}:{endpoint.Port}/health/live");

            var response = await Http.GetAsync(url, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"KurrentDB health check returned {response.StatusCode}.");
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
