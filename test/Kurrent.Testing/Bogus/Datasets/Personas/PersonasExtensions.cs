using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.Personas;

public static class PersonasExtensions {
    extension(Faker faker) {
        public PersonasDataSet PunnyPersonas() =>
            ContextHelper.GetOrSet(faker, f => new PersonasDataSet(f));
    }
}
