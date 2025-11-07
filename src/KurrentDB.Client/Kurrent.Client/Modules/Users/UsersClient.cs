// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grpc.Core;
using Kurrent.Grpc;
using KurrentDB.Client;
using KurrentDB.Protocol.V1.Users;
using AsyncStreamReaderExtensions = Kurrent.Grpc.AsyncStreamReaderExtensions;
using UsersServiceClient = KurrentDB.Protocol.V1.Users.Users.UsersClient;

namespace Kurrent.Client.Users;

public class UsersClient : ClientModuleBase {
    internal UsersClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.LegacyCallInvoker);

    UsersServiceClient ServiceClient { get; }

    public async ValueTask<Result<Success, CreateUserError>> Create(
        LoginName loginName, string fullName, string[] groups, string password,
        CancellationToken cancellationToken = default
    ) {
        try {
            var request = new CreateReq {
                Options = new() {
                    LoginName = loginName,
                    FullName  = fullName,
                    Password  = password,
                    Groups    = { groups }
                }
            };

            await ServiceClient
                .CreateAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (RpcException rex) {
            return Result.Failure<Success, CreateUserError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.AlreadyExists    => new ErrorDetails.AlreadyExists(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<Success, DeleteUserError>> Delete(LoginName loginName, CancellationToken cancellationToken = default) {
        try {
            var request = new DeleteReq { Options = new() { LoginName = loginName } };

            await ServiceClient
                .DeleteAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (RpcException rex) {
            return Result.Failure<Success, DeleteUserError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.NotFound         => new ErrorDetails.NotFound(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<Success, DisableUserError>> Disable(LoginName loginName, CancellationToken cancellationToken = default) {
        try {
            var request = new DisableReq { Options = new() { LoginName = loginName } };

            await ServiceClient
                .DisableAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (RpcException rex) {
            return Result.Failure<Success, DisableUserError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.NotFound         => new ErrorDetails.NotFound(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<Success, EnableUserError>> Enable(LoginName loginName, CancellationToken cancellationToken = default) {
        try {
            var request = new EnableReq { Options = new() { LoginName = loginName } };

            await ServiceClient
                .EnableAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (RpcException rex) {
            return Result.Failure<Success, EnableUserError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.NotFound         => new ErrorDetails.NotFound(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<Success, ChangeUserPasswordError>> ChangePassword(
        LoginName loginName, string currentPassword, string newPassword, CancellationToken cancellationToken = default
    ) {
        try {
            var request = new ChangePasswordReq {
                Options = new() {
                    CurrentPassword = currentPassword,
                    NewPassword     = newPassword,
                    LoginName       = loginName
                }
            };

            await ServiceClient
                .ChangePasswordAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (RpcException rex) {
            return Result.Failure<Success, ChangeUserPasswordError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.NotFound         => new ErrorDetails.NotFound(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<Success, ResetUserPasswordError>> ResetPassword(
        LoginName loginName, string newPassword, CancellationToken cancellationToken = default
    ) {
        try {
            var request = new ResetPasswordReq {
                Options = new() {
                    NewPassword = newPassword,
                    LoginName   = loginName
                }
            };

            // TODO SS: does the user exist? do we want to be explicit about this?

            await ServiceClient
                .ResetPasswordAsync(request, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Results.Success;
        }
        catch (RpcException rex) {
            return Result.Failure<Success, ResetUserPasswordError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.NotFound         => new ErrorDetails.NotFound(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<UserDetails, GetUserError>> GetDetails(LoginName loginName, CancellationToken cancellationToken = default) {
        try {
            var request = new DetailsReq { Options = new() { LoginName = loginName } };

            using var call = ServiceClient.Details(request, cancellationToken: cancellationToken);

            var user = await AsyncStreamReaderExtensions.ReadAllAsync(call.ResponseStream, cancellationToken)
                .Select(static resp => new UserDetails {
                    LoginName       = resp.UserDetails.LoginName,
                    FullName        = resp.UserDetails.FullName,
                    Groups          = resp.UserDetails.Groups.ToArray(),
                    Disabled        = resp.UserDetails.Disabled,
                    DateLastUpdated = resp.UserDetails.LastUpdated?.TicksSinceEpoch.FromTicksSinceEpoch()
                })
                .FirstOrDefaultAsync(cancellationToken: cancellationToken);

            return user ?? Result.Failure<UserDetails, GetUserError>(new ErrorDetails.NotFound());
        }
        catch (RpcException rex) {
            return Result.Failure<UserDetails, GetUserError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                StatusCode.NotFound         => new ErrorDetails.NotFound(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    public async ValueTask<Result<List<UserDetails>, ListAllUsersError>> List(CancellationToken cancellationToken = default) {
        try {
            using var call = ServiceClient.Details(new DetailsReq(), cancellationToken: cancellationToken);

            var users = await AsyncStreamReaderExtensions.ReadAllAsync(call.ResponseStream, cancellationToken)
                .Select(static resp => new UserDetails { // TODO SS: Create a mapper for mapping user details
                    LoginName       = resp.UserDetails.LoginName,
                    FullName        = resp.UserDetails.FullName,
                    Groups          = resp.UserDetails.Groups.ToArray(),
                    Disabled        = resp.UserDetails.Disabled,
                    DateLastUpdated = resp.UserDetails.LastUpdated?.TicksSinceEpoch.FromTicksSinceEpoch()
                })
                .ToListAsync(cancellationToken: cancellationToken);

            return users;
        }
        catch (RpcException rex) {
            return Result.Failure<List<UserDetails>, ListAllUsersError>(rex.StatusCode switch {
                StatusCode.PermissionDenied => new ErrorDetails.AccessDenied(),
                _                           => throw rex.WithOriginalCallStack()
            });
        }
    }

    /// <summary>
    /// Enables stream policy authorization for fine-grained access control based on stream prefixes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When stream policies are enabled, traditional Access Control Lists (ACLs) on stream metadata
    /// are no longer enforced. The server will use policy-based authorization instead.
    /// </para>
    /// <para>
    /// Default policies are automatically created on first enable, granting public access to user streams
    /// and restricting system streams to administrators.
    /// </para>
    /// </remarks>
    public async ValueTask<Result<Success, EnableStreamPoliciesError>> EnableStreamPolicies(
        CancellationToken cancellationToken = default
    ) {
        using var client = GetBackdoorClient();

        try {
            var request = new HttpRequestMessage(HttpMethod.Post, "/streams/$authorization-policy-settings") {
                Content = JsonContent.Create(new { streamAccessPolicyType = "streampolicy" })
            };
            request.Headers.Add("ES-EventId", Guid.NewGuid().ToString());
            request.Headers.Add("ES-EventType", "$authorization-policy-changed");

            var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            return Success.Instance;
        }
        catch (HttpRequestException hex) {
            return Result.Failure<Success, EnableStreamPoliciesError>(hex.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ErrorDetails.AccessDenied(),
                HttpStatusCode.BadRequest                               => new ErrorDetails.BadRequest(),
                _                                                       => throw hex.WithOriginalCallStack()
            });
        }
    }

    /// <summary>
    /// Disables stream policy authorization and reverts to Access Control Lists (ACLs).
    /// </summary>
    /// <remarks>
    /// After disabling, the server will use traditional per-stream ACL metadata
    /// for authorization decisions.
    /// </remarks>
    public async ValueTask<Result<Success, DisableStreamPoliciesError>> DisableStreamPolicies(
        CancellationToken cancellationToken = default
    ) {
        using var client = GetBackdoorClient();

        try {
            var request = new HttpRequestMessage(HttpMethod.Post, "/streams/$authorization-policy-settings") {
                Content = JsonContent.Create(new { streamAccessPolicyType = "acl" })
            };
            request.Headers.Add("ES-EventId", Guid.NewGuid().ToString());
            request.Headers.Add("ES-EventType", "$authorization-policy-changed");

            var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            return Success.Instance;
        }
        catch (HttpRequestException hex) {
            return Result.Failure<Success, DisableStreamPoliciesError>(hex.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ErrorDetails.AccessDenied(),
                HttpStatusCode.BadRequest                               => new ErrorDetails.BadRequest(),
                _                                                       => throw hex.WithOriginalCallStack()
            });
        }
    }

    /// <summary>
    /// Configures stream policies, defining access rules for stream prefixes.
    /// </summary>
    /// <param name="configuration">Policy configuration including policies, rules, and defaults.</param>
    /// <param name="validate">Whether to validate configuration before sending (default: true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>
    /// This operation replaces the entire policy configuration. To update incrementally,
    /// first retrieve the current configuration with <see cref="GetCurrentStreamPolicy"/>,
    /// modify it, and then configure.
    /// </para>
    /// </remarks>
    public async ValueTask<Result<Success, ConfigureStreamPoliciesError>> ConfigureStreamPolicies(
        StreamPolicyConfiguration configuration,
        bool validate = true,
        CancellationToken cancellationToken = default
    ) {
        if (validate)
            configuration.Validate();

        using var client = GetBackdoorClient();

        try {
            // Map to server schema (note the special JSON property names with $ prefix)
            var serverPayload = new {
                streamPolicies = configuration.StreamPolicies.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ServerAccessPolicy {
                        Readers         = kvp.Value.Readers,
                        Writers         = kvp.Value.Writers,
                        Deleters        = kvp.Value.Deleters,
                        MetadataReaders = kvp.Value.MetadataReaders,
                        MetadataWriters = kvp.Value.MetadataWriters
                    }
                ),
                streamRules = configuration.StreamRules.Select(r => new {
                    startsWith = r.Prefix,
                    policy     = r.PolicyName
                }),
                defaultStreamRules = new {
                    userStreams   = configuration.DefaultRules.UserStreams,
                    systemStreams = configuration.DefaultRules.SystemStreams
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/streams/$policies") {
                Content = JsonContent.Create(serverPayload, options: PolicySerializationOptions)
            };
            request.Headers.Add("ES-EventId", Guid.NewGuid().ToString());
            request.Headers.Add("ES-EventType", "$policy-updated");

            var response = await client
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            return Success.Instance;
        }
        catch (HttpRequestException hex) {
            return Result.Failure<Success, ConfigureStreamPoliciesError>(hex.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ErrorDetails.AccessDenied(),
                HttpStatusCode.BadRequest                               => new ErrorDetails.BadRequest(),
                _                                                       => throw hex.WithOriginalCallStack()
            });
        }
    }

    /// <summary>
    /// Retrieves the currently active stream policy configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Returns the most recent policy event from the $policies stream.
    /// If no policies are configured, returns a <see cref="ErrorDetails.NotFound"/> error.
    /// </remarks>
    public async ValueTask<Result<StreamPolicyConfiguration, GetStreamPoliciesError>> GetCurrentStreamPolicy(
        CancellationToken cancellationToken = default
    ) {
        using var client = GetBackdoorClient();

        try {
            // Read latest event from $policies stream with body embedded
            var response = await client
                .GetFromJsonAsync<ServerPolicyStreamResponse>("/streams/$policies/head/backward/1?embed=body", PolicySerializationOptions, cancellationToken)
                .ConfigureAwait(false);

            if (response?.Entries == null || response.Entries.Length == 0)
                return Result.Failure<StreamPolicyConfiguration, GetStreamPoliciesError>(new ErrorDetails.NotFound());

            var policyData = response.Entries[0].Data;
            if (policyData == null)
                return Result.Failure<StreamPolicyConfiguration, GetStreamPoliciesError>(new ErrorDetails.NotFound());

            // Map from server schema to client model
            var config = new StreamPolicyConfiguration {
                StreamPolicies = policyData.StreamPolicies.ToImmutableDictionary(
                    kvp => kvp.Key,
                    kvp => new StreamAccessPolicy {
                        Readers         = kvp.Value.Readers,
                        Writers         = kvp.Value.Writers,
                        Deleters        = kvp.Value.Deleters,
                        MetadataReaders = kvp.Value.MetadataReaders,
                        MetadataWriters = kvp.Value.MetadataWriters
                    }
                ),
                StreamRules = policyData.StreamRules.Select(r => new StreamPrefixRule(r.StartsWith, r.Policy)).ToImmutableArray(),
                DefaultRules = new DefaultPolicyRules {
                    UserStreams   = policyData.DefaultStreamRules.UserStreams,
                    SystemStreams = policyData.DefaultStreamRules.SystemStreams
                }
            };

            return config;
        }
        catch (HttpRequestException hex) {
            return Result.Failure<StreamPolicyConfiguration, GetStreamPoliciesError>(hex.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ErrorDetails.AccessDenied(),
                HttpStatusCode.NotFound                                 => new ErrorDetails.NotFound(),
                _                                                       => throw hex.WithOriginalCallStack()
            });
        }
    }

    static readonly JsonSerializerOptions PolicySerializationOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Internal server schema mapping classes
    sealed record ServerAccessPolicy {
        [JsonPropertyName("$r")]
        public string[] Readers { get; init; } = [];

        [JsonPropertyName("$w")]
        public string[] Writers { get; init; } = [];

        [JsonPropertyName("$d")]
        public string[] Deleters { get; init; } = [];

        [JsonPropertyName("$mr")]
        public string[] MetadataReaders { get; init; } = [];

        [JsonPropertyName("$mw")]
        public string[] MetadataWriters { get; init; } = [];
    }

    sealed record ServerStreamRule {
        [JsonPropertyName("startsWith")]
        public string StartsWith { get; init; } = "";

        [JsonPropertyName("policy")]
        public string Policy { get; init; } = "";
    }

    sealed record ServerDefaultStreamRules {
        [JsonPropertyName("userStreams")]
        public string UserStreams { get; init; } = "";

        [JsonPropertyName("systemStreams")]
        public string SystemStreams { get; init; } = "";
    }

    sealed record ServerPolicyData {
        [JsonPropertyName("streamPolicies")]
        public Dictionary<string, ServerAccessPolicy> StreamPolicies { get; init; } = new();

        [JsonPropertyName("streamRules")]
        public ServerStreamRule[] StreamRules { get; init; } = [];

        [JsonPropertyName("defaultStreamRules")]
        public ServerDefaultStreamRules DefaultStreamRules { get; init; } = new();
    }

    sealed record ServerPolicyStreamEntry {
        [JsonPropertyName("data")]
        public ServerPolicyData? Data { get; init; }
    }

    sealed record ServerPolicyStreamResponse {
        [JsonPropertyName("entries")]
        public ServerPolicyStreamEntry[]? Entries { get; init; }
    }
}
