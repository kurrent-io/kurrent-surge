using Bogus;
using Bogus.Platform;
using static System.StringComparison;

namespace Testing.Datasets;

/// <summary>
/// Represents a customizable data set for generating mock data.
/// This class serves as the foundation for creating domain-specific datasets
/// by leveraging the Bogus library's data generation utilities.
/// </summary>
/// <remarks>
/// Each dataset should use a bson file to store its data, unless it is 100% generated.
/// </remarks>
/// <typeparam name="T">The type of the dataset class inheriting from this base.</typeparam>
public abstract class CustomDataSet<T> : DataSet {
    protected CustomDataSet(Faker faker) {
        Faker = faker;
        Initialize();
    }

    protected Faker Faker { get; }

    protected virtual void Initialize() {
        var resourceName = $"Bogus.{typeof(T).Name.Replace("DataSet", "", OrdinalIgnoreCase)}.data.en.locale.bson";
        var asm          = typeof(T).GetAssembly();

        var obj    = ResourceHelper.ReadBObjectResource(asm, resourceName);
        var locale = Database.GetLocale("en");

        foreach (var val in obj.Keys)
            locale[val] = obj[val];
    }
}

public static class ContextHelper {
    public static T GetOrSet<T>(string key, Faker faker, Func<Faker, T> createDs) where T : DataSet {
        var context = ((IHasContext)faker).Context;

        if (context.TryGetValue(key, out var t) && t is T ds)
            return ds;

        var dataset  = createDs(faker);
        var notifier = ((IHasRandomizer)faker).GetNotifier();

        notifier.Flow(dataset);

        context[key] = dataset;

        return dataset;
    }

    public static T GetOrSet<T>(Faker faker, Func<Faker, T> createDs) where T : DataSet {
        var key = typeof(T).Name.ToLowerInvariant();
        return GetOrSet($"__{key}", faker, createDs);
    }
}
