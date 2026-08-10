using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using KurrentDB.Client;

namespace Kurrent.Surge.Client.Legacy;

/// <summary>
/// A gRPC CallInvoker implementation that delegates to the LegacyClusterClient to obtain
/// channel information. This provides a bridge between the legacy gossip-based discovery
/// and modern gRPC client patterns.
/// </summary>
sealed class KurrentDBLegacyCallInvoker : CallInvoker, IAsyncDisposable {
	readonly LegacyClusterClient _legacyClient;
	readonly bool                _disposeClient;

	volatile ChannelSnapshot? _snapshot;

	bool _disposed;

	internal KurrentDBLegacyCallInvoker(KurrentClientOptions options, Interceptor[]? prepend = null, bool disposeClient = true) {
		var settings = options.ConvertToLegacySettings();

		if (prepend is { Length: > 0 })
			settings.Interceptors = [..prepend, ..settings.Interceptors];

		_legacyClient  = LegacyClusterClient.Create(settings);
		_disposeClient = disposeClient;
	}

	/// <summary>
	/// Gets the current server capabilities from the connected node.
	/// </summary>
	public ServerCapabilities ServerCapabilities {
		[SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
		get => TryGetCachedChannel()?.ServerCapabilities
			?? throw new InvalidOperationException(
				"Ensure the client is connected before accessing server capabilities.");
	}

	/// <summary>
	/// Gets the current channel options used for gRPC calls.
	/// </summary>
	public GrpcChannelOptions ChannelOptions {
		[SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
		get => TryGetCachedChannel()?.Options
			?? throw new InvalidOperationException(
				"Ensure the client is connected before accessing channel options.");
	}

	/// <summary>
	/// Gets the current channel target (the address of the connected node).
	/// </summary>
	public string ChannelTarget {
		[SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
		get => TryGetCachedChannel()?.Channel.Target
			?? throw new InvalidOperationException(
				"Ensure the client is connected before accessing the channel target.");
	}

	/// <summary>
	/// Pre-warms the connection by resolving the channel info ahead of the first gRPC call.
	/// </summary>
	public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		var info = await _legacyClient.Connect(cancellationToken).ConfigureAwait(false);
		UpdateSnapshot(info);
	}

	/// <summary>
	/// Forces a refresh of the internal channel info and server capabilities by attempting to reconnect to the cluster.
	/// </summary>
	public async Task ForceRefresh(CancellationToken cancellationToken) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		var channelInfo = await _legacyClient.ForceReconnect().ConfigureAwait(false);
		UpdateSnapshot(channelInfo);
	}

	ChannelInfo? TryGetCachedChannel() {
		var snap = _snapshot;
		if (snap is not null && ReferenceEquals(snap.Source, _legacyClient.CurrentChannelAsync))
			return snap.Info;

		return null;
	}

	async Task<TResult> ResolveAndCallAsync<TResult>(CancellationToken ct, Func<CallInvoker, TResult> invoke) {
		ObjectDisposedException.ThrowIf(_disposed, this);
		var channelInfo = await _legacyClient.Connect(ct).ConfigureAwait(false);
		UpdateSnapshot(channelInfo);
		return invoke(channelInfo.CallInvoker);
	}

	void UpdateSnapshot(ChannelInfo info) =>
		_snapshot = new(Source: _legacyClient.CurrentChannelAsync, Info: info);

	#region ->> CallInvoker Overrides <<-

	/// <inheritdoc/>
	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
	) {
		var cached = TryGetCachedChannel();
		if (cached is not null)
			return cached.CallInvoker.AsyncUnaryCall(method, host, options, request);

		var ct      = options.CancellationToken;
		var pending = ResolveAndCallAsync(ct, inv => inv.AsyncUnaryCall(method, host, options, request));

		return new(
			pending.ContinueWith(static t => t.Result.ResponseAsync, TaskContinuationOptions.ExecuteSynchronously).Unwrap(),
			pending.ContinueWith(static t => t.Result.ResponseHeadersAsync, TaskContinuationOptions.ExecuteSynchronously).Unwrap(),
			() => pending.Result.GetStatus(),
			() => pending.Result.GetTrailers(),
			() => { if (pending.IsCompletedSuccessfully) pending.Result.Dispose(); }
		);
	}

	/// <inheritdoc/>
	public override TResponse BlockingUnaryCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
	) {
		var cached = TryGetCachedChannel();
		if (cached is not null)
			return cached.CallInvoker.BlockingUnaryCall(method, host, options, request);

		var info = _legacyClient.Connect(options.CancellationToken).GetAwaiter().GetResult();
		UpdateSnapshot(info);
		return info.CallInvoker.BlockingUnaryCall(method, host, options, request);
	}

	/// <inheritdoc/>
	public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options
	) {
		var cached = TryGetCachedChannel();
		if (cached is not null)
			return cached.CallInvoker.AsyncClientStreamingCall(method, host, options);

		var ct      = options.CancellationToken;
		var pending = ResolveAndCallAsync(ct, inv => inv.AsyncClientStreamingCall(method, host, options));

		return new(
			new DeferredStreamWriter<TRequest>(pending.ContinueWith(
				static t => t.Result.RequestStream, TaskContinuationOptions.ExecuteSynchronously)),
			pending.ContinueWith(static t => t.Result.ResponseAsync, TaskContinuationOptions.ExecuteSynchronously).Unwrap(),
			pending.ContinueWith(static t => t.Result.ResponseHeadersAsync, TaskContinuationOptions.ExecuteSynchronously).Unwrap(),
			() => pending.Result.GetStatus(),
			() => pending.Result.GetTrailers(),
			() => { if (pending.IsCompletedSuccessfully) pending.Result.Dispose(); }
		);
	}

	/// <inheritdoc/>
	public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
	) {
		var cached = TryGetCachedChannel();
		if (cached is not null)
			return cached.CallInvoker.AsyncServerStreamingCall(method, host, options, request);

		var ct      = options.CancellationToken;
		var pending = ResolveAndCallAsync(ct, inv => inv.AsyncServerStreamingCall(method, host, options, request));

		return new(
			new DeferredStreamReader<TResponse>(pending.ContinueWith(
				static t => t.Result.ResponseStream, TaskContinuationOptions.ExecuteSynchronously)),
			pending.ContinueWith(static t => t.Result.ResponseHeadersAsync, TaskContinuationOptions.ExecuteSynchronously).Unwrap(),
			() => pending.Result.GetStatus(),
			() => pending.Result.GetTrailers(),
			() => { if (pending.IsCompletedSuccessfully) pending.Result.Dispose(); }
		);
	}

	/// <inheritdoc/>
	public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options
	) {
		var cached = TryGetCachedChannel();
		if (cached is not null)
			return cached.CallInvoker.AsyncDuplexStreamingCall(method, host, options);

		var ct      = options.CancellationToken;
		var pending = ResolveAndCallAsync(ct, inv => inv.AsyncDuplexStreamingCall(method, host, options));

		return new(
			new DeferredStreamWriter<TRequest>(pending.ContinueWith(
				static t => t.Result.RequestStream, TaskContinuationOptions.ExecuteSynchronously)),
			new DeferredStreamReader<TResponse>(pending.ContinueWith(
				static t => t.Result.ResponseStream, TaskContinuationOptions.ExecuteSynchronously)),
			pending.ContinueWith(static t => t.Result.ResponseHeadersAsync, TaskContinuationOptions.ExecuteSynchronously).Unwrap(),
			() => pending.Result.GetStatus(),
			() => pending.Result.GetTrailers(),
			() => { if (pending.IsCompletedSuccessfully) pending.Result.Dispose(); }
		);
	}

	#endregion

	#region ->> Deferred Stream Adapters <<-

	sealed class DeferredStreamReader<T>(Task<IAsyncStreamReader<T>> pending) : IAsyncStreamReader<T> {
		IAsyncStreamReader<T>? _inner;

		public T Current => (_inner ?? throw new InvalidOperationException("Call MoveNext first.")).Current;

		public async Task<bool> MoveNext(CancellationToken cancellationToken) {
			_inner ??= await pending.ConfigureAwait(false);
			return await _inner.MoveNext(cancellationToken).ConfigureAwait(false);
		}
	}

	sealed class DeferredStreamWriter<T>(Task<IClientStreamWriter<T>> pending) : IClientStreamWriter<T> {
		IClientStreamWriter<T>? _inner;

		public WriteOptions? WriteOptions {
			get => _inner?.WriteOptions;
			set {
				if (_inner is not null) _inner.WriteOptions = value;
			}
		}

		public async Task WriteAsync(T message) {
			_inner ??= await pending.ConfigureAwait(false);
			await _inner.WriteAsync(message).ConfigureAwait(false);
		}

		public async Task CompleteAsync() {
			_inner ??= await pending.ConfigureAwait(false);
			await _inner.CompleteAsync().ConfigureAwait(false);
		}
	}

	#endregion

	/// <inheritdoc/>
	public async ValueTask DisposeAsync() {
		if (_disposed)
			return;

		_disposed = true;

		if (_disposeClient)
			await _legacyClient.DisposeAsync().ConfigureAwait(false);
	}

	sealed record ChannelSnapshot(Task<ChannelInfo> Source, ChannelInfo Info);
}
