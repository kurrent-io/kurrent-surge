using System.Net;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client;

/// <summary>
/// Represents a parsed KurrentDB connection string with all its components.
/// Internal detail behind <see cref="KurrentClientOptions.Parse(string)"/>.
/// </summary>
record KurrentConnectionString {
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

    const string DefaultDirectScheme    = "kurrentdb";
    const string DefaultDiscoveryScheme = "kurrentdb+discover";

    internal static readonly string[] DiscoverySchemes = [DefaultDiscoveryScheme, "esdb+discover", "eventstore+discover"];
    internal static readonly string[] AllSchemes       = [DefaultDirectScheme, "esdb", "eventstore", ..DiscoverySchemes];

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

    /// <summary>
    /// Gets the original connection string passed to the <see cref="KurrentConnectionString"/> instance.
    /// This property preserves the exact string supplied during initialization, allowing it to be used for troubleshooting,
    /// debugging, or recreating the connection settings.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Gets the scheme component of the connection string associated with the <see cref="KurrentConnectionString"/> instance.
    /// This property identifies the communication protocol or access mechanism
    /// used for connecting to the target resource (e.g., "http", "https", "tcp").
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Gets the user information, including the username and password, associated with the connection string.
    /// This property returns a tuple containing the user and password components if present, or null if no user credentials
    /// were specified in the connection string.
    /// </summary>
    public (string User, string Password)? UserInfo { get; }

    /// <summary>
    /// Retrieves the collection of hosts associated with the current connection.
    /// Each host is represented as a <see cref="DnsEndPoint"/> instance, specifying
    /// the hostname and port details for communication with the Kurrent database cluster.
    /// </summary>
    public DnsEndPoint[] Hosts   { get; }

    /// <summary>
    /// Gets the collection of key-value pairs representing additional options or parameters
    /// specified in the connection string. These options provide the ability to configure
    /// or extend the behavior of the connection beyond standard properties.
    /// </summary>
    public Dictionary<string, string> Options { get; }

    /// <summary>
    /// Parses the provided connection string and returns an instance of the <see cref="KurrentConnectionString"/> class.
    /// </summary>
    /// <param name="connectionString">
    /// A string specifying the connection string to be parsed. Must not be null, empty, or whitespace.
    /// </param>
    /// <returns>
    /// A new instance of the <see cref="KurrentConnectionString"/> class, initialized with the values
    /// extracted from the given connection string.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the <paramref name="connectionString"/> is null, empty, or consists only of whitespace.
    /// </exception>
    /// <exception cref="InvalidConnectionStringException">
    /// Thrown if the format of the connection string is invalid or required components are missing.
    /// </exception>
    internal static KurrentConnectionString Parse(string connectionString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var currentIndex = 0;

        var schemeIndex = connectionString.IndexOf(SchemeSeparator, currentIndex, StringComparison.Ordinal);
        if (schemeIndex == -1)
            throw new InvalidConnectionStringException("Could not parse scheme from connection string.");

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
            throw new InvalidConnectionStringException("Connection string must specify at least one endpoint.");

        if (hosts.Length > 1 && !DiscoverySchemes.Contains(scheme))
            throw new InvalidConnectionStringException("Direct connection scheme does not support multiple hosts.");

        currentIndex = hostSeparatorIndex;

        var path = "";
        if (slashIndex != int.MaxValue)
            path = connectionString.Substring(
                currentIndex,
                Math.Min(questionMarkIndex, endIndex) - currentIndex
            );

        if (path != "" && path != "/")
            throw new InvalidConnectionStringException("Invalid path in connection string.");

        var options = new Dictionary<string, string>();

        if (questionMarkIndex != int.MaxValue) {
            currentIndex = questionMarkIndex + 1;
            options      = ParseParameters(connectionString[currentIndex..]);
        }

        return new(
            connectionString, scheme, userInfo,
            hosts, options
        );

        static string ParseScheme(string input) =>
            AllSchemes.Contains(input)
                ? input
                : throw new InvalidConnectionStringException("Invalid connection scheme.");

        static (string, string) ParseUserInfo(string input) {
            var tokens = input.Split(Colon);
            if (tokens.Length != 2)
                throw new InvalidConnectionStringException("Invalid user credentials format.");

            var username = Uri.UnescapeDataString(tokens[0]);
            var password = Uri.UnescapeDataString(tokens[1]);

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                throw new InvalidConnectionStringException("Invalid user credentials format.");

            return (username, password);
        }

        static DnsEndPoint[] ParseHosts(string input) {
            var hosts = input.Split(Comma).Select(hostToken => {
                    // Check for malformed host:port combinations (e.g., "host:" with trailing colon but no port)
                    if (hostToken.EndsWith(Colon) && !hostToken.EndsWith("::")) // Allow IPv6 addresses like "::1"
                        throw new InvalidConnectionStringException("Invalid host format.");

                    // address can be in the form of "host:port" or just "host"
                    var token = hostToken.Split(Colon, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    var endpoint = token.Length switch {
                        1 => new(token[0], DefaultPort),
                        2 => int.TryParse(token[1], out var port) && port is > 0 and <= 65535
                            ? new DnsEndPoint(token[0], port)
                            : throw new InvalidConnectionStringException("Invalid host format."),
                        _ => throw new InvalidConnectionStringException("Invalid host format.")
                    };

                    return endpoint;
                }
            ).ToArray();

            return hosts.Length == 0 ? throw new InvalidConnectionStringException("Invalid host format.") : hosts;
        }

        static Dictionary<string, string> ParseParameters(string input) {
            return input.Split(Ampersand).Aggregate(
                new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase),
                (seed, option) => {
                    var (key, value) = ParseParameter(option);
                    return !seed.TryAdd(key, value)
                        ? throw new InvalidConnectionStringException("Duplicate parameter key in connection string.")
                        : seed;
                }
            );

            static (string, string) ParseParameter(string input) {
                var keyValueToken = input.Split(Equal);
                return keyValueToken.Length != 2
                    ? throw new InvalidConnectionStringException("Invalid key-value pair format.")
                    : (keyValueToken[0], Uri.UnescapeDataString(keyValueToken[1]));
            }
        }
    }
}

public class InvalidConnectionStringException(string reason, Exception? innerException = null)
    : KurrentException(Code, reason, innerException) {
    static readonly ErrorCode Code = ErrorCode.FromType<InvalidConnectionStringException>();
}
