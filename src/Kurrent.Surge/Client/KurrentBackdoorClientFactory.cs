using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Kurrent.Http;
using Kurrent.Surge.Client.LoadBalancing;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Kurrent.Surge.Client;

/// <summary>
/// Factory for creating HTTP clients that communicate with the Kurrent backdoor service.
/// By default, it will create a client that connects to the current target node of the
/// <see cref="ClusterChannel"/>.
/// </summary>
class KurrentBackdoorClientFactory : IDisposable {
    public KurrentBackdoorClientFactory(ClusterChannel channel, KurrentClientOptions options, ClientInitializer initializer) {
        Channel = channel;
        Logger  = options.LoggerFactory.CreateLogger("KurrentBackdoorClient");

        if (options.Security.Transport.IsInsecure)
            HttpSecurity.EnableHttp2UnencryptedSupport();

        ParseTargetNodeUri = CreateTargetNodeUriParser(options.HttpUriScheme);

        BackdoorHandler = new FaultGateHttpHandler(initializer) {
            InnerHandler = new EnsureSuccessStatusHandler {
                InnerHandler = new ResilienceHandler(CreateResiliencePipeline(options.Resilience, Logger)) {
                    InnerHandler = CreatePrimaryHandler(options.Security, options.Resilience, Logger)
                }
            }
        };
    }

    ClusterChannel     Channel            { get; }
    ILogger            Logger             { get; }
    HttpMessageHandler BackdoorHandler    { get; }
    Func<string, Uri>  ParseTargetNodeUri { get; }

    public HttpClient GetClient(Uri address) {
        return new(BackdoorHandler, disposeHandler: false) {
            BaseAddress           = address,
            Timeout               = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrHigher
        };
    }

    public HttpClient GetClient(string targetNode) => GetClient(ParseTargetNodeUri(targetNode));

    /// <summary>
    /// Gets a new HTTP client that connects to the current target node of the <see cref="ClusterChannel"/>.
    /// </summary>
    /// <remarks>
    /// Always dispose the returned <see cref="HttpClient"/> after use to avoid resource leaks.
    /// </remarks>
    /// <returns></returns>
    public HttpClient GetClient() => GetClient(Channel.Target);

    public void Dispose() => BackdoorHandler.Dispose();

    static Func<string, Uri> CreateTargetNodeUriParser(string httpUriScheme) =>
        target => new($"{httpUriScheme}://{target}");

    static ResiliencePipeline<HttpResponseMessage> CreateResiliencePipeline(KurrentClientResilienceOptions options, ILogger logger) {
        var httpResilienceOptions = new HttpStandardResilienceOptions {
            AttemptTimeout = {
                Timeout   = options.Retry.MaxBackoff,
                OnTimeout = arguments => {
                    logger.LogWarning("Operation timed out after {Timeout}", arguments.Timeout);
                    return ValueTask.CompletedTask;
                }
            },
            Retry = {
                MaxRetryAttempts = options.Retry.MaxAttempts,
                Delay            = options.Retry.InitialBackoff,
                MaxDelay         = options.Retry.MaxBackoff,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                OnRetry          = retryArguments => {
                    if (retryArguments.Outcome.Result is not null) {
                        var url    = retryArguments.Outcome.Result.RequestMessage!.RequestUri!.ToString();
                        var method = retryArguments.Outcome.Result.RequestMessage.Method.ToString();

                        logger.LogDebug(
                            "Retrying after {Delay} due to response: {StatusCode} {ResponseMessage} for {Method} request to {Url}. Retry attempt {AttemptNumber}",
                            retryArguments.Duration, (int)retryArguments.Outcome.Result.StatusCode, retryArguments.Outcome.Result.ReasonPhrase, method,
                            url, retryArguments.AttemptNumber + 1
                        );
                    }

                    return ValueTask.CompletedTask;
                }
            }
        };

        if (options.Deadline is { } deadline)
            httpResilienceOptions.TotalRequestTimeout.Timeout = deadline;

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRateLimiter(httpResilienceOptions.RateLimiter)
            .AddTimeout(httpResilienceOptions.TotalRequestTimeout)
            .AddRetry(httpResilienceOptions.Retry)
            .AddCircuitBreaker(httpResilienceOptions.CircuitBreaker)
            .AddTimeout(httpResilienceOptions.AttemptTimeout)
            .Build();
    }

    static SocketsHttpHandler CreatePrimaryHandler(KurrentClientSecurityOptions security, KurrentClientResilienceOptions resilience, ILogger logger) {
        var handler = new SocketsHttpHandler {
            KeepAlivePingDelay             = resilience.KeepAliveInterval,
            KeepAlivePingTimeout           = resilience.KeepAliveTimeout,
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression         = DecompressionMethods.All
        };

        if (security.Transport.IsInsecure) {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            return handler;
        }

        if (security.Authentication.TryGetCertificate(out var clientCertificate))
            handler.SslOptions.ClientCertificates = new() { clientCertificate };

        if (security.Transport.VerifyServerCertificate && security.Transport.TryGetCertificate(out var caCertificate))
            handler.SslOptions.RemoteCertificateValidationCallback = new RemoteCertificateValidator(caCertificate, logger);

        return handler;
    }
}
