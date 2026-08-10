using System.Net;
using Grpc.Core;
using Grpc.Net.Client.Balancer;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Routes every call to the most preferred node, as ranked by <see cref="GossipResolver"/>.
/// Holds a single connection like the legacy selector: one subchannel over the ranked
/// address list, so the transport falls back through lower-ranked nodes when the target
/// is unreachable. When the top-ranked target changes (leader moved), the subchannel is
/// replaced so traffic follows the new target instead of sticking to the old connection.
/// </summary>
sealed partial class NodePreferenceBalancer(IChannelControlHelper controller, ILoggerFactory loggerFactory) : LoadBalancer {
    readonly ILogger<NodePreferenceBalancer> _log = loggerFactory.CreateLogger<NodePreferenceBalancer>();

    Subchannel?       _subchannel;
    DnsEndPoint?      _currentTarget;
    ConnectivityState _state;

    /// <summary>
    /// The endpoint of the subchannel's live transport, or <see langword="null"/> when
    /// no connection has been established yet.
    /// </summary>
    public DnsEndPoint? ConnectedEndpoint => _subchannel?.CurrentAddress?.EndPoint;

    public override void UpdateChannelState(ChannelState state) {
        if (state.Status.StatusCode != StatusCode.OK) {
            ResolverError(state.Status);
            return;
        }

        if (state.Addresses is null || state.Addresses.Count == 0) {
            ResolverError(new(StatusCode.Unavailable, "Resolver returned no addresses."));
            return;
        }

        var target = state.Addresses[0].EndPoint;

        if (_subchannel is not null && DnsEndPointComparer.Instance.Equals(target, _currentTarget)) {
            _subchannel.UpdateAddresses(state.Addresses);
            return;
        }

        // New or moved target: replace the subchannel so the connection follows it. The
        // subchannel gets the full ranked list and its transport tries addresses in order.
        RemoveSubchannel();

        var created = controller.CreateSubchannel(new(state.Addresses));
        created.OnStateChanged(subchannelState => UpdateSubchannelState(created, subchannelState));

        _subchannel    = created;
        _currentTarget = target;

        UpdateState(ConnectivityState.Connecting, new QueuePicker());
        created.RequestConnection();
    }

    public override void RequestConnection() => _subchannel?.RequestConnection();

    void UpdateSubchannelState(Subchannel subchannel, SubchannelState state) {
        if (!ReferenceEquals(_subchannel, subchannel))
            return;

        switch (state.State) {
            case ConnectivityState.Ready:
                UpdateState(ConnectivityState.Ready, new ConnectedPicker(subchannel));
                break;

            case ConnectivityState.Idle:
                controller.RefreshResolver();
                UpdateState(ConnectivityState.Idle, new RequestConnectionPicker(subchannel));
                break;

            case ConnectivityState.Connecting:
                UpdateState(ConnectivityState.Connecting, new QueuePicker());
                break;

            case ConnectivityState.TransientFailure:
                UpdateState(ConnectivityState.TransientFailure, new FailurePicker(state.Status));
                break;

            case ConnectivityState.Shutdown:
                UpdateState(ConnectivityState.Shutdown, new QueuePicker());
                _subchannel    = null;
                _currentTarget = null;
                break;
        }
    }

    void ResolverError(Status status) {
        // A live connection survives resolver failures; only unconnected states surface them.
        switch (_state) {
            case ConnectivityState.Idle:
            case ConnectivityState.Connecting:
            case ConnectivityState.TransientFailure:
                RemoveSubchannel();
                _currentTarget = null;
                UpdateState(ConnectivityState.TransientFailure, new FailurePicker(status));
                break;

            default:
                LogResolverErrorIgnored(_log, status.StatusCode, status.Detail);
                break;
        }
    }

    void UpdateState(ConnectivityState state, SubchannelPicker picker) {
        _state = state;
        controller.UpdateState(new(state, picker));
    }

    void RemoveSubchannel() {
        _subchannel?.Dispose();
        _subchannel = null;
    }

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        RemoveSubchannel();
    }

    #region ->> Pickers <<-

    sealed class ConnectedPicker(Subchannel subchannel) : SubchannelPicker {
        public override PickResult Pick(PickContext context) => PickResult.ForSubchannel(subchannel);
    }

    sealed class RequestConnectionPicker(Subchannel subchannel) : SubchannelPicker {
        public override PickResult Pick(PickContext context) {
            subchannel.RequestConnection();
            return PickResult.ForSubchannel(subchannel);
        }
    }

    sealed class QueuePicker : SubchannelPicker {
        public override PickResult Pick(PickContext context) => PickResult.ForQueue();
    }

    sealed class FailurePicker(Status status) : SubchannelPicker {
        public override PickResult Pick(PickContext context) => PickResult.ForFailure(status);
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolver error ignored while connected: {StatusCode} {Detail}")]
    static partial void LogResolverErrorIgnored(ILogger logger, StatusCode statusCode, string detail);
}
