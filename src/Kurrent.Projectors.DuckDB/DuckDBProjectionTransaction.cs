using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public sealed class DuckDBProjectionTransaction(IDuckDBConnectionProvider provider) : IAsyncDisposable {
    DuckDBAdvancedConnection? _connection;
    IDisposable?              _rental;
    TransactionScope          _transaction;
    bool                      _isOpen;

    public ValueTask<DuckDBAdvancedConnection> Open(CancellationToken cancellationToken) {
        if (_connection is null) {
            _rental     = provider.GetScopedConnection(out var connection);
            _connection = connection;
        }

        if (!_isOpen) {
            _transaction = _connection.BeginTransaction();
            _isOpen      = true;
        }

        return ValueTask.FromResult(_connection);
    }

    public ValueTask Commit(CancellationToken cancellationToken) {
        if (!_isOpen)
            throw new InvalidOperationException("No transaction is open. Call Open() first.");

        _transaction.CommitOnDispose();
        Close();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() {
        if (_isOpen)
            Close();

        _rental?.Dispose();

        _rental     = null;
        _connection = null;

        return ValueTask.CompletedTask;
    }

    void Close() {
        ((IDisposable)_transaction).Dispose();

        _transaction = default;
        _isOpen      = false;
    }
}
