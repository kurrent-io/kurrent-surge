using KurrentDB.Client;

namespace KurrentDB.Client.Tests;

[Collection(KurrentDBCollection.Name)]
public class AppendToStreamTests(KurrentDBFixture fixture) {
    [Fact]
    public async Task append_single_event_to_new_stream() {
        await using var client = fixture.CreateClient();

        var stream    = $"test-{Guid.NewGuid():N}";
        var eventData = new EventData(Uuid.NewUuid(), "TestEvent", """{"key":"value"}"""u8.ToArray());

        var result = await client.AppendToStreamAsync(
            stream,
            StreamState.NoStream,
            [eventData]
        );

        Assert.True(result.NextExpectedStreamRevision >= 0);
    }

    [Fact]
    public async Task append_and_read_back_events() {
        await using var client = fixture.CreateClient();

        var stream = $"test-{Guid.NewGuid():N}";

        var events = Enumerable
            .Range(0, 10)
            .Select(i => new EventData(Uuid.NewUuid(), "TestEvent", $$"""{"index":{{i}}}"""u8.ToArray()))
            .ToArray();

        await client.AppendToStreamAsync(stream, StreamState.NoStream, events);

        var readResult = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start);
        var readEvents = await readResult.ToListAsync();

        Assert.Equal(10, readEvents.Count);

        for (var i = 0; i < readEvents.Count; i++)
            Assert.Equal("TestEvent", readEvents[i].Event.EventType);
    }

    [Fact]
    public async Task append_with_wrong_expected_version_throws() {
        await using var client = fixture.CreateClient();

        var stream    = $"test-{Guid.NewGuid():N}";
        var eventData = new EventData(Uuid.NewUuid(), "TestEvent", """{}"""u8.ToArray());

        await client.AppendToStreamAsync(stream, StreamState.NoStream, [eventData]);

        var secondEvent = new EventData(Uuid.NewUuid(), "TestEvent", """{}"""u8.ToArray());

        await Assert.ThrowsAsync<WrongExpectedVersionException>(
            () => client.AppendToStreamAsync(stream, StreamState.NoStream, [secondEvent])
        );
    }
}
