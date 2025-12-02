namespace Kurrent.Client.Streams;

[PublicAPI]
public class AppendStreamRequestBuilder {
    ExpectedStreamState  _expectedState   = ExpectedStreamState.Any;
    List<AppendRecordBuilder> _messageBuilders = [];

    string _stream = "";

    public AppendStreamRequestBuilder ForStream(string stream) {
        _stream = stream;
        return this;
    }

    public AppendStreamRequestBuilder ExpectingRevision(StreamRevision expectedRevision) {
        _expectedState = expectedRevision;
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(AppendRecord record) =>
        WithRecord(builder => builder.FromRecord(record));

    public AppendStreamRequestBuilder WithRecord(Action<AppendRecordBuilder> configureBuilder) {
        var messageBuilder = new AppendRecordBuilder();
        configureBuilder(messageBuilder);
        _messageBuilders.Add(messageBuilder);
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(AppendRecordBuilder appendRecordBuilder) {
        _messageBuilders.Add(appendRecordBuilder);
        return this;
    }

    public AppendStreamRequestBuilder WithRecord(object value, SchemaDataFormat dataFormat = SchemaDataFormat.Json, Metadata? metadata = null) =>
        WithRecord(builder => builder.WithValue(value).WithDataFormat(dataFormat).WithMetadata(metadata ?? new Metadata()));

    public int MessageCount => _messageBuilders.Count;

    public AppendStreamRequest Build() {
        var messages = _messageBuilders.Select(x => x.Build());
        var request  = new AppendStreamRequest(_stream, _expectedState, messages);
        return request;
    }
}

[PublicAPI]
public class AppendRecordBuilder {
	AppendRecord _appendRecord = new() {
		RecordId   = Guid.NewGuid(),
		Value      = null!,
		DataFormat = SchemaDataFormat.Json,
        Metadata   = new Metadata()
        // Metadata   = new Metadata().With(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Json)
	};

    public AppendRecordBuilder FromRecord(AppendRecord record) {
        _appendRecord = record;
        return this;
    }

    public AppendRecordBuilder WithRecordId(Guid recordId) {
		if (_appendRecord.RecordId == Guid.Empty)
			throw new ArgumentNullException(nameof(recordId), "RecordId cannot be empty");

		_appendRecord = _appendRecord with { RecordId = recordId };
		return this;
	}

	public AppendRecordBuilder WithValue(object value) {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "Message value cannot be null");

		_appendRecord = _appendRecord with { Value = value };

		return this;
	}

	public AppendRecordBuilder WithDataFormat(SchemaDataFormat dataFormat) {
		if (dataFormat == SchemaDataFormat.Unspecified)
			throw new ArgumentNullException(nameof(dataFormat), "Data format cannot be unspecified");

		if (_appendRecord.Value.IsBytes() && dataFormat != SchemaDataFormat.Bytes)
			throw new ArgumentException($"Data format must be {SchemaDataFormat.Bytes} because the value is a byte array");

		_appendRecord = _appendRecord with {
			DataFormat = dataFormat
		};

		return this;
	}

	// public AppendRecordBuilder WithMetadata(Metadata metadata) {
	// 	var dataFormat = metadata.GetSchemaFormat();
	// 	if (dataFormat == SchemaDataFormat.Unspecified)
	// 		dataFormat = _appendRecord.DataFormat;
	//
	// 	_appendRecord = _appendRecord with {
	// 		DataFormat = dataFormat,
	// 		Metadata   = metadata.CreateUnlockedCopy()
	// 			.With(SystemMetadataKeys.SchemaFormat, dataFormat)
	// 	};
	//
	// 	return this;
	// }

    public AppendRecordBuilder WithMetadata(Metadata metadata) {
        _appendRecord = _appendRecord with {
            Metadata   = metadata
        };

        return this;
    }

	public AppendRecordBuilder WithMetadata<T>(string key, T value) =>
		WithMetadata(_appendRecord.Metadata.With(key, value));

	public AppendRecordBuilder WithMetadata(IDictionary<string, object?> entries) =>
		WithMetadata(_appendRecord.Metadata.WithMany(entries));

	public AppendRecord Build() {
		if (_appendRecord.Value is null)
			throw new InvalidOperationException("Message value cannot be null");

		// // Create a copy of metadata to avoid modifying the original
		// var metadata = _appendRecord.Metadata.CreateUnlockedCopy()
		// 	.With(SystemMetadataKeys.SchemaFormat, _appendRecord.DataFormat);
		//
		// return _appendRecord with { Metadata = metadata };

        return _appendRecord;
    }
}
