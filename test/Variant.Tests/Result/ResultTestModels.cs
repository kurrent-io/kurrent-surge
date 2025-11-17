using System.Collections.Immutable;
using TicTacToe;

namespace Kurrent.Variant.Tests.Result;

public readonly record struct GameId(Guid Value);

public record GameStarted(Guid GameId, Player FirstPlayer);

// public record PlayerTurn(Player Player, Position Position, string? MoveDescription = null);
//
// public record GameUpdated(Guid GameId, GameStatus NewStatus, string? UpdateNotes = null);

public record InvalidMoveError(Guid GameId, string Reason, string? StateContext = null) : IResultError {
	public ErrorCode ErrorCode => ErrorCode.From("INVALID_MOVE");

	public string ErrorMessage => Reason;

	public ImmutableDictionary<string, object?> Metadata =>
		ImmutableDictionary<string, object?>.Empty
			.Add("GameId", GameId)
			.Add("StateContext", StateContext ?? "unknown");

	public Exception GetOrCreateException() =>
		new InvalidOperationException($"Invalid move in game {GameId}: {Reason}");

	public void Throw() => throw GetOrCreateException();
}

public record GameEndedError(Guid GameId, GameStatus Status, string? FinalStateContext = null) : IResultError {
	public ErrorCode ErrorCode => ErrorCode.From("GAME_ENDED");

	public string ErrorMessage => $"Game {GameId} has ended with status: {Status}";

	public ImmutableDictionary<string, object?> Metadata =>
		ImmutableDictionary<string, object?>.Empty
			.Add("GameId", GameId)
			.Add("Status", Status.ToString())
			.Add("FinalStateContext", FinalStateContext ?? "unknown");

	public Exception GetOrCreateException() =>
		new InvalidOperationException($"Game {GameId} has ended with status: {Status}");

	public void Throw() => throw GetOrCreateException();
}

public record SimpleError(string Message) : IResultError {
	public ErrorCode ErrorCode => ErrorCode.From("SIMPLE_ERROR");

	public string ErrorMessage => Message;

	public ImmutableDictionary<string, object?> Metadata => ImmutableDictionary<string, object?>.Empty;

	public Exception GetOrCreateException() =>
		new InvalidOperationException(Message);

	public void Throw() => throw GetOrCreateException();
}
