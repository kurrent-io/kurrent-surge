using System.Diagnostics;
using Kurrent.Client.Streams;
using Serilog;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class ConcurrencyStressTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	[Timeout(30000)]
	public async ValueTask concurrent_appends_do_not_starve_thread_pool(CancellationToken ct) {
		// Arrange
		const int concurrency = 50;

		await using var client = Fixture.GetNewClient();

		var tasks = new Task<AppendStreamResponse>[concurrency];

		// Act
		var sw = Stopwatch.StartNew();

		for (var i = 0; i < concurrency; i++) {
			var data = await Fixture.TestData.GenerateSet(ct);
			tasks[i] = client.Streams
				.AppendStream(AppendStreamRequest.For(data.Stream).WithRecords(data.Records).Build(), ct)
				.AsTask();
		}

		var results = await Task.WhenAll(tasks);
		sw.Stop();

		// Assert
		await Assert.That(results.Length).IsEqualTo(concurrency);

		foreach (var result in results)
			await Assert.That(result.Position.Value).IsGreaterThanOrEqualTo(0);

		Log.Information(
			"Completed {Count} concurrent appends in {Elapsed}ms",
			concurrency, sw.ElapsedMilliseconds);
	}

	[Test]
	[Timeout(30000)]
	public async ValueTask concurrent_reads_do_not_starve_thread_pool(CancellationToken ct) {
		// Arrange
		const int concurrency = 50;

		await using var client = Fixture.GetNewClient();

		var seeded = await Fixture.SeedTestStream(5, ct);

		var tasks = new Task<int>[concurrency];

		var readRequest = ReadStreamRequest.New
			.ForStream(seeded.Stream)
			.SkipDecoding()
			.Build();

		// Act
		var sw = Stopwatch.StartNew();

		for (var i = 0; i < concurrency; i++) {
			tasks[i] = client.Streams
				.ReadStream(readRequest, ct)
				.Where(msg => msg.IsRecord)
				.CountAsync(ct)
				.AsTask();
		}

		var counts = await Task.WhenAll(tasks);
		sw.Stop();

		// Assert
		foreach (var count in counts)
			await Assert.That(count).IsEqualTo(seeded.Records.Length);

		Log.Information(
			"Completed {Count} concurrent reads in {Elapsed}ms",
			concurrency, sw.ElapsedMilliseconds);
	}

	[Test]
	[Timeout(30000)]
	public async ValueTask interleaved_reads_and_writes_do_not_starve_thread_pool(CancellationToken ct) {
		// Arrange
		const int concurrency = 30;

		await using var client = Fixture.GetNewClient();

		var tasks = new List<Task>(concurrency * 2);

		// Act
		var sw = Stopwatch.StartNew();

		for (var i = 0; i < concurrency; i++) {
			var data = await Fixture.TestData.GenerateSet(ct);

			var stream = data.Stream;
			var expectedCount = data.Records.Length;

			tasks.Add(client.Streams
				.AppendStream(AppendStreamRequest.For(stream).WithRecords(data.Records).Build(), ct)
				.AsTask()
				.ContinueWith(async appendTask => {
					await appendTask;

					var request = ReadStreamRequest.New.ForStream(stream).SkipDecoding().Build();

					var count = await client.Streams
						.ReadStream(request, ct)
						.Where(msg => msg.IsRecord)
						.CountAsync(ct);

					await Assert.That(count).IsEqualTo(expectedCount);
				}, ct).Unwrap());
		}

		await Task.WhenAll(tasks);
		sw.Stop();

		// Assert
		await Assert.That(tasks.Count).IsEqualTo(concurrency);

		Log.Information(
			"Completed {Count} interleaved append+read pairs in {Elapsed}ms",
			concurrency, sw.ElapsedMilliseconds);
	}

	[Test]
	[Timeout(30000)]
	public async ValueTask many_clients_resolve_channels_without_blocking(CancellationToken ct) {
		// Arrange
		const int clientCount = 10;
		const int opsPerClient = 5;

		var clients = Enumerable.Range(0, clientCount)
			.Select(_ => Fixture.GetNewClient())
			.ToArray();

		var tasks = new List<Task>(clientCount * opsPerClient);

		// Act
		var sw = Stopwatch.StartNew();

		foreach (var client in clients) {
			for (var i = 0; i < opsPerClient; i++) {
				var data = await Fixture.TestData.GenerateSet(ct);
				tasks.Add(client.Streams
					.AppendStream(AppendStreamRequest.For(data.Stream).WithRecords(data.Records).Build(), ct)
					.AsTask());
			}
		}

		await Task.WhenAll(tasks);
		sw.Stop();

		// Assert
		await Assert.That(tasks.All(t => t.IsCompletedSuccessfully)).IsTrue();

		Log.Information(
			"Completed {Count} ops across {Clients} fresh clients in {Elapsed}ms",
			tasks.Count, clientCount, sw.ElapsedMilliseconds);

		// Cleanup
		foreach (var client in clients)
			await client.DisposeAsync();
	}

	[Test]
	[Timeout(30000)]
	public async ValueTask thread_pool_stays_healthy_under_concurrent_load(CancellationToken ct) {
		// Arrange
		const int concurrency = 100;

		await using var client = Fixture.GetNewClient();

		ThreadPool.GetMinThreads(out var minWorker, out _);
		ThreadPool.GetAvailableThreads(out var availableBefore, out var _ioIgnored);

		var tasks = new Task[concurrency];

		// Act
		for (var i = 0; i < concurrency; i++) {
			var data = await Fixture.TestData.GenerateSet(ct);
			tasks[i] = client.Streams
				.AppendStream(AppendStreamRequest.For(data.Stream).WithRecords(data.Records).Build(), ct)
				.AsTask();
		}

		await Task.WhenAll(tasks);

		ThreadPool.GetAvailableThreads(out var availableAfter, out var _ioIgnored2);

		// Assert — thread starvation would show available dropping near zero
		var starvationThreshold = minWorker / 2;

		Log.Information(
			"Thread pool: min={Min}, before={Before}, after={After}, threshold={Threshold}",
			minWorker, availableBefore, availableAfter, starvationThreshold);

		await Assert.That(availableAfter).IsGreaterThan(starvationThreshold);
	}
}
