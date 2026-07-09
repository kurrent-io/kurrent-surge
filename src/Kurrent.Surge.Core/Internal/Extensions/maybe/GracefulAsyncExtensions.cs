namespace Kurrent.Surge.Client;

  static class GracefulAsyncExtensions {
      extension(Task task) {
          /// <summary>
          /// Waits for task completion. Returns false if cancelled (no throw).
          /// </summary>
          public async ValueTask<bool> WaitUnlessCancelledAsync(CancellationToken ct) {
              try {
                  await task.WaitAsync(ct).ConfigureAwait(false);
                  return true;
              }
              catch (OperationCanceledException) {
                  return false;
              }
          }
      }

      extension<T>(IAsyncEnumerator<T> enumerator) {
          /// <summary>
          /// Moves to next item. Returns true if moved, false if completed or cancelled.
          /// </summary>
          public async ValueTask<bool> MoveNextUnlessCancelledAsync() {
              try {
                  return await enumerator.MoveNextAsync().ConfigureAwait(false);
              }
              catch (OperationCanceledException) {
                  return false;
              }
          }
      }
  }
