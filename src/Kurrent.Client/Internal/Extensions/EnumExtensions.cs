using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace Kurrent.Client;

static class EnumExtensions {
    static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Enum, string>> Cache = new();

    extension<T>(T source) where T : Enum {
        public string Description {
            get {
                // Get or create the value dictionary for this enum type
                var valueCache = Cache.GetOrAdd(typeof(T), _ => new ConcurrentDictionary<Enum, string>());

                // Cast once to avoid multiple boxing operations
                Enum enumValue = source;

                return valueCache.GetOrAdd(enumValue, key => {
                    var fieldInfo = typeof(T).GetField(key.ToString());

                    return fieldInfo is null
                        ? key.ToString()
                        : fieldInfo.GetCustomAttribute<DescriptionAttribute>(false)?.Description ?? key.ToString();
                });
            }
        }
    }
}
