# Processor.WordCount

The classic "Hello World" of stream processing - counting word frequencies across a stream of text.

## What You'll Learn

- Stateful aggregation with in-memory state
- Grouping and counting patterns
- Using `ConcurrentDictionary` for thread-safe accumulation
- Logging aggregated results at checkpoints

## How It Works

1. **Input**: `TextSubmitted` events containing text content
2. **Processing**: Extract words, normalize to lowercase, update frequency counts
3. **Output**: Logs top 10 words at each checkpoint

```
TextSubmitted("The quick brown fox")
    → {the: 1, quick: 1, brown: 1, fox: 1}

TextSubmitted("The fox jumped")
    → {the: 2, quick: 1, brown: 1, fox: 2, jumped: 1}
```

## Prerequisites

- KurrentDB running on `localhost:2113`

## Running the Example

```bash
cd examples/processors/Processor.WordCount
dotnet run
```

A hosted `FactSeeder` fetches a random fact from `uselessfacts.jsph.pl` every second and appends it
as a `TextSubmitted` event to a per-run stream, which the processor subscribes to. Press `Ctrl+C`
to stop.

## Key Code

```csharp
public sealed partial class WordCountProcessor : MessageProcessor {
    public WordCountProcessor() {
        Process<TextSubmitted>((msg, ctx) => {
            var words = WordPattern().Matches(msg.Content)
                .Select(m => m.Value.ToLowerInvariant())
                .Where(w => w.Length > 1);

            foreach (var word in words)
                _wordCounts.AddOrUpdate(word, 1, (_, count) => count + 1);
        });
    }
}
```

## Sample Output

```
[INF] Checkpoint #5 | Texts: 100 | Words: 2847 | Unique: 412
[INF] Top 10 words:
[INF]   the: 287
[INF]   and: 156
[INF]   to: 134
[INF]   a: 98
[INF]   of: 87
...
```

## Original Tutorials

- [Apache Flink Word Count](https://github.com/apache/flink/blob/master/flink-examples/flink-examples-streaming/src/main/java/org/apache/flink/streaming/examples/wordcount/WordCount.java)
- [Kafka Streams Word Count](https://kafka.apache.org/documentation/streams/)
