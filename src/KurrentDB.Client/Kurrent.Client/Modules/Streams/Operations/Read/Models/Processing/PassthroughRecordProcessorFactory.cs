namespace Kurrent.Client.Streams;

class PassthroughRecordProcessorFactory(IRecordProcessor processor) : IRecordProcessorFactory {
    public IRecordProcessor CreateProcessor() => processor;
}
