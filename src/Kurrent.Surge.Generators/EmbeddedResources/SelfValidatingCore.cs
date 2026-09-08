// The shared plumbing every [SelfValidating] type receives. Authored as ordinary C# and
// transplanted member-by-member into the annotated partial type — never inherited, so the
// type keeps its base-class slot.
//
// This file is an EmbeddedResource, not Compile. It is parsed at generation time.

public abstract class SelfValidatingCore
{
    /// <summary>
    /// The cached verdict. Null means "not yet computed, or invalidated by a change".
    /// </summary>
    private global::Kurrent.Surge.Validation.ValidationResult? __validationResult;

    /// <inheritdoc cref="global::System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Discards the cached verdict and notifies listeners. Called by every generated setter,
    /// and by the collection and child-options subscriptions.
    /// </summary>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        __validationResult = null;
        PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Invalidates without raising a property-level notification. Used when a nested options
    /// object or a wrapped collection reports a change of its own.
    /// </summary>
    protected void OnDependencyChanged()
    {
        __validationResult = null;
    }

    /// <summary>
    /// Wraps a caller-supplied sequence in an observable collection and subscribes to it, so that
    /// Add, Remove, Clear and indexer assignment all invalidate. The copy also means the caller's
    /// own collection is never aliased.
    /// </summary>
    protected global::System.Collections.Generic.IList<T> __Observe<T>(global::System.Collections.Generic.IEnumerable<T>? source)
    {
        global::System.Collections.ObjectModel.ObservableCollection<T> collection =
            source is null
                ? new global::System.Collections.ObjectModel.ObservableCollection<T>()
                : new global::System.Collections.ObjectModel.ObservableCollection<T>(source);

        collection.CollectionChanged += __OnCollectionChanged;

        return collection;
    }

    /// <summary>
    /// Detaches a previously wrapped collection so a replaced instance stops invalidating.
    /// </summary>
    protected void __Detach<T>(global::System.Collections.Generic.IList<T>? collection)
    {
        if (collection is global::System.Collections.ObjectModel.ObservableCollection<T> observable)
        {
            observable.CollectionChanged -= __OnCollectionChanged;
        }
    }

    private void __OnCollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnDependencyChanged();
    }

    private void __OnChildChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnDependencyChanged();
    }

    /// <summary>
    /// Subscribes to a nested options object so its changes invalidate this one.
    /// </summary>
    /// <remarks>
    /// Idempotent. A property initializer assigns the backing field directly, so the getter has to
    /// be able to attach a child it may already have attached; removing a handler that was never
    /// added is a no-op, which makes the pair safe to repeat.
    /// </remarks>
    protected T __Attach<T>(T child) where T : class
    {
        if (child is global::System.ComponentModel.INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= __OnChildChanged;
            notifier.PropertyChanged += __OnChildChanged;
        }

        return child;
    }

    /// <summary>
    /// Unsubscribes from a nested options object being replaced.
    /// </summary>
    protected void __DetachChild<T>(T? child) where T : class
    {
        if (child is global::System.ComponentModel.INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= __OnChildChanged;
        }
    }

    /// <summary>
    /// Returns the cached verdict, computing it on first call or after any change.
    /// </summary>
    public global::Kurrent.Surge.Validation.ValidationResult Validate()
    {
        return __validationResult ??= __RunValidation();
    }

    /// <summary>
    /// Runs validation and throws when the object is invalid, logging warning-severity problems
    /// to <paramref name="logger"/> when one is supplied.
    /// </summary>
    /// <exception cref="global::System.Exception">
    /// Thrown when validation reports an error-severity problem. The exception type and message
    /// come from the failing rules.
    /// </exception>
    public void EnsureValid(global::Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        global::Kurrent.Surge.Validation.ValidationResult result = Validate();

        if (logger is null)
        {
            result.EnsureValid();
        }
        else
        {
            result.EnsureValid(logger);
        }
    }

    /// <summary>
    /// Supplied by the generated per-type half; runs the configured rules.
    /// </summary>
    protected abstract global::Kurrent.Surge.Validation.ValidationResult __RunValidation();
}
