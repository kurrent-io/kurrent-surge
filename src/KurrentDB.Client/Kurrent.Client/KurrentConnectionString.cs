using System.Net;
using Kurrent.Client.Exceptions;
using Resources;

namespace Kurrent.Client;

/// <summary>
/// Represents a parsed KurrentDB connection string with all its components
/// </summary>
public record KurrentConnectionString {
    const string SchemeSeparator = "://";

    const char UserInfoSeparator = '@';
    const char Colon             = ':';
    const char Slash             = '/';
    const char Comma             = ',';
    const char Ampersand         = '&';
    const char Equal             = '=';
    const char QuestionMark      = '?';

    const int DefaultPort = 2113;

    // Make constants and fields internal so they can be accessed by the extension method
    internal const string Tls                  = nameof(Tls);
    internal const string ConnectionName       = nameof(ConnectionName);
    internal const string MaxDiscoverAttempts  = nameof(MaxDiscoverAttempts);
    internal const string DiscoveryInterval    = nameof(DiscoveryInterval);
    internal const string GossipTimeout        = nameof(GossipTimeout);
    internal const string NodePreference       = nameof(NodePreference);
    internal const string TlsVerifyCert        = nameof(TlsVerifyCert);
    internal const string TlsCaFile            = nameof(TlsCaFile);
    internal const string DefaultDeadline      = nameof(DefaultDeadline);
    internal const string ThrowOnAppendFailure = nameof(ThrowOnAppendFailure);
    internal const string KeepAliveInterval    = nameof(KeepAliveInterval);
    internal const string KeepAliveTimeout     = nameof(KeepAliveTimeout);
    internal const string UserCertFile         = nameof(UserCertFile);
    internal const string UserKeyFile          = nameof(UserKeyFile);

    internal const           string   DefaultDirectScheme    = "kurrentdb";
    internal const           string   DefaultDiscoveryScheme = "kurrentdb+discover";
    internal static readonly string[] DiscoverySchemes       = [DefaultDiscoveryScheme, "esdb+discover", "eventstore+discover"];
    internal static readonly string[] AllSchemes             = [DefaultDirectScheme, "esdb", "eventstore", ..DiscoverySchemes];

    KurrentConnectionString(
        string connectionString, string scheme, (string User, string Password)? userInfo, DnsEndPoint[] hosts,
        Dictionary<string, string> options
    ) {
        ConnectionString = connectionString;
        Scheme           = scheme;
        UserInfo         = userInfo;
        Hosts            = hosts;
        Options          = options;
    }

    public string                          ConnectionString { get; }
    public string                          Scheme           { get; }
    public (string User, string Password)? UserInfo         { get; }
    public DnsEndPoint[]                   Hosts            { get; }
    public Dictionary<string, string>      Options          { get; }

    public static KurrentConnectionString Parse(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var currentIndex = 0;

        var schemeIndex = connectionString.IndexOf(SchemeSeparator, currentIndex, StringComparison.Ordinal);
        if (schemeIndex == -1)
            throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringNoScheme);

        var scheme = ParseScheme(connectionString[..schemeIndex]);

        currentIndex = schemeIndex + SchemeSeparator.Length;

        var userInfoIndex = connectionString.IndexOf(UserInfoSeparator, currentIndex);

        (string User, string Password)? userInfo = null;
        if (userInfoIndex != -1) {
            userInfo     = ParseUserInfo(connectionString.Substring(currentIndex, userInfoIndex - currentIndex));
            currentIndex = userInfoIndex + 1;
        }

        var slashIndex        = connectionString.IndexOf(Slash, currentIndex);
        var questionMarkIndex = connectionString.IndexOf(QuestionMark, currentIndex);
        var endIndex          = connectionString.Length;

        //for simpler substring operations:
        if (slashIndex == -1)
            slashIndex = int.MaxValue;

        if (questionMarkIndex == -1)
            questionMarkIndex = int.MaxValue;

        var hostSeparatorIndex = Math.Min(Math.Min(slashIndex, questionMarkIndex), endIndex);
        var hosts              = ParseHosts(connectionString.Substring(currentIndex, hostSeparatorIndex - currentIndex));

        if (hosts.Length == 0)
            throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringNoEndpoints);

        if (hosts.Length > 1 && scheme == DefaultDirectScheme)
            throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringMultipleHostsDirectScheme);

        currentIndex = hostSeparatorIndex;

        var path = "";
        if (slashIndex != int.MaxValue)
            path = connectionString.Substring(
                currentIndex,
                Math.Min(questionMarkIndex, endIndex) - currentIndex
            );

        if (path != "" && path != "/")
            throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidPath);

        var options = new Dictionary<string, string>();

        if (questionMarkIndex != int.MaxValue) {
            currentIndex = questionMarkIndex + 1;
            options      = ParseParameters(connectionString[currentIndex..]);
        }

        return new KurrentConnectionString(
            connectionString, scheme, userInfo,
            hosts, options
        );

        static string ParseScheme(string input) =>
            AllSchemes.Contains(input)
                ? input.ToLowerInvariant()
                : throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidScheme);

        static (string, string) ParseUserInfo(string input) {
            var tokens = input.Split(Colon);
            if (tokens.Length != 2)
                throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidUserCredentials);

            var username = Uri.UnescapeDataString(tokens[0]);
            var password = Uri.UnescapeDataString(tokens[1]);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidUserCredentials);

            return (username, password);
        }

        static DnsEndPoint[] ParseHosts(string input) {
            var hosts = input.Split(Comma).Select(hostToken => {
                    // Check for malformed host:port combinations (e.g., "host:" with trailing colon but no port)
                    if (hostToken.EndsWith(Colon) && !hostToken.EndsWith("::")) // Allow IPv6 addresses like "::1"
                        throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidHost);

                    // address can be in the form of "host:port" or just "host"
                    var token = hostToken.Split(Colon, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    var endpoint = token.Length switch {
                        1 => new DnsEndPoint(token[0], DefaultPort),
                        2 => new DnsEndPoint(token[0], int.TryParse(token[1], out var port) ? port : throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidHost)),
                        _ => throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidHost)
                    };

                    return endpoint;
                }
            ).ToArray();

            return hosts.Length == 0 ? throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidHost) : hosts;
        }

        static Dictionary<string, string> ParseParameters(string input) {
            return input.Split(Ampersand).Aggregate(
                new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase),
                (seed, option) => {
                    var (key, value) = ParseParameter(option);
                    return !seed.TryAdd(key, value)
                        ? throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringDuplicateKey)
                        : seed;
                }
            );

            static (string, string) ParseParameter(string input) {
                var keyValueToken = input.Split(Equal);
                return keyValueToken.Length != 2
                    ? throw new InvalidConnectionStringException(ErrorMessages.ConnectionStringInvalidKeyValuePair)
                    : (keyValueToken[0], Uri.UnescapeDataString(keyValueToken[1]));
            }
        }
    }
}

public class InvalidConnectionStringException(string message, Exception? innerException = null)
    : KurrentException(Code, message, innerException) {
    static readonly ErrorCode Code = ErrorCode.FromType<InvalidConnectionStringException>();
}
