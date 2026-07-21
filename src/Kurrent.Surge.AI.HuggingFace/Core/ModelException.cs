namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>Raised for any model-acquisition failure (resolution, listing, download, or integrity).</summary>
public sealed class ModelException : Exception {
    public ModelException(string message) : base(message) { }

    public ModelException(string message, Exception inner) : base(message, inner) { }
}
