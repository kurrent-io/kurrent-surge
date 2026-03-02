using System.Reflection;
using Bogus;

namespace Kurrent.Testing.Bogus.Datasets;

/// <summary>
/// Root object for premium data sets.
/// </summary>
public abstract class ResourceBasedDataSet : DataSet {
    protected abstract void Initialize();

    protected void LoadResource(Assembly asm, string resourceName) {
        var obj = ResourceHelper.ReadBObjectResource(asm, resourceName);
        //patch
        var enLocale = Database.GetLocale("en");

        foreach (var val in obj.Keys) enLocale[val] = obj[val];
    }
}
