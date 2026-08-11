using Bogus;
using Google.Protobuf;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Legacy;
using KurrentDB.Protocol.V1;

using ReadEvent = KurrentDB.Protocol.V1.Streams.ReadResp.Types.ReadEvent;
using RecordedEvent = KurrentDB.Protocol.V1.Streams.ReadResp.Types.ReadEvent.Types.RecordedEvent;

namespace Kurrent.Surge.Tests.Modules.Streams;

//TODO SS: This is the way to create fakers. Must refactor it all in one go.

sealed class FakeRecordedEvent : Faker<RecordedEvent> {
    public static FakeRecordedEvent New => new();

    FakeRecordedEvent() {
        RuleFor(x => x.Id, f => Uuid.FromGuid(f.Random.Guid()).ToDto());
        RuleFor(x => x.StreamIdentifier, f => (StreamIdentifier)f.Random.AlphaNumeric(12));
        RuleFor(x => x.StreamRevision, f => f.Random.ULong(0, 1000));
        RuleFor(x => x.CommitPosition, f => f.Random.ULong(1000, 100000));
        RuleFor(x => x.Data, _ => ByteString.CopyFromUtf8("{\"value\":1}"));
        RuleFor(x => x.CustomMetadata, _ => ByteString.Empty);

        FinishWith((f, x) => {
            x.Metadata["type"]         = "TestEvent";
            x.Metadata["created"]      = f.Date.RecentOffset().Ticks.ToString();
            x.Metadata["content-type"] = "application/json";
        });
    }
}

static class FakeRecordedEventExtensions {
    extension(FakeRecordedEvent faker) {
        public Faker<RecordedEvent> AsLinkEvent() =>
            faker
                .RuleFor(x => x.StreamIdentifier, f => (StreamIdentifier)$"$et-{f.Random.AlphaNumeric(8)}")
                .RuleFor(x => x.Data, _ => ByteString.CopyFromUtf8("0@test-stream"))
                .FinishWith((f, x) => {
                    x.Metadata["type"]         = "$>";
                    x.Metadata["created"]      = f.Date.RecentOffset().Ticks.ToString();
                    x.Metadata["content-type"] = "application/octet-stream";
                });
    }
}

sealed class FakeReadEvent : Faker<ReadEvent> {
    public static FakeReadEvent New => new();

    FakeReadEvent() {
        CustomInstantiator(_ => new());

        FinishWith((_, x) => {
            x.Event          = FakeRecordedEvent.New.Generate();
            x.CommitPosition = x.Event.CommitPosition;
        });
    }
}

static class FakeReadEventExtensions {
    extension(FakeReadEvent faker) {
        public Faker<ReadEvent> WithResolvedLink() =>
            faker.FinishWith((_, x) => {
                x.Event          = FakeRecordedEvent.New.Generate();
                x.Link           = FakeRecordedEvent.New.AsLinkEvent().Generate();
                x.CommitPosition = x.Event.CommitPosition;
            });

        public Faker<ReadEvent> WithBrokenLink() =>
            faker.FinishWith((_, x) => {
                x.Link           = FakeRecordedEvent.New.AsLinkEvent().Generate();
                x.CommitPosition = x.Link.CommitPosition;
            });

        public Faker<ReadEvent> WithoutLinkOrEvent() =>
            faker.RuleFor(x => x.CommitPosition, f => f.Random.ULong(50000, 990000));
    }
}
