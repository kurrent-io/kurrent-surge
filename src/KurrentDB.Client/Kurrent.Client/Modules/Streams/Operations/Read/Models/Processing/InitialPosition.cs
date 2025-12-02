namespace Kurrent.Client.Streams;

public enum InitialPosition {
    /// <summary>
    /// Consumption will start at the last message.
    /// </summary>
    Latest = 0,

    /// <summary>
    /// Consumption will start at the first message.
    /// </summary>
    Earliest = 1
}
