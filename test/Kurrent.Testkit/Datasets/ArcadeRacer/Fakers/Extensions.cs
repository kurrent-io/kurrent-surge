using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;

#region Models

public static class RaceCarExtensions {
    extension(RaceCar) {
        public static RaceCarFaker New(Faker? faker = null) => new(faker);

        public static RaceCar Generate(Faker? faker = null) =>
            new RaceCarFaker(faker).Generate();

        public static IEnumerable<RaceCar> GenerateForever(Faker? faker = null) =>
            RaceCar.New(faker).GenerateForever();

        public static List<RaceCar> Generate(int count, Faker? faker = null) =>
            RaceCar.GenerateForever(faker).Take(count).ToList();
    }
}

public static class CircuitExtensions {
    extension(Circuit) {
        public static CircuitFaker New(Faker? faker = null) => new(faker);

        public static Circuit Generate(Faker? faker = null) =>
            new CircuitFaker(faker).Generate();

        public static IEnumerable<Circuit> GenerateForever(Faker? faker = null) =>
            Circuit.New(faker).GenerateForever();

        public static List<Circuit> Generate(int count, Faker? faker = null) =>
            Circuit.GenerateForever(faker).Take(count).ToList();
    }
}

#endregion

#region Events

public static class GridFormedExtensions {
    extension(GridFormed) {
        public static GridFormedFaker New(Faker? faker = null) => new(faker);

        public static GridFormed Generate(Faker? faker = null) =>
            new GridFormedFaker(faker).Generate();

        public static IEnumerable<GridFormed> GenerateForever(Faker? faker = null) =>
            GridFormed.New(faker).GenerateForever();

        public static List<GridFormed> Generate(int count, Faker? faker = null) =>
            GridFormed.GenerateForever(faker).Take(count).ToList();
    }
}

public static class RaceStartedExtensions {
    extension(RaceStarted) {
        public static RaceStartedFaker New(Faker? faker = null) => new(faker);

        public static RaceStarted Generate(Faker? faker = null) =>
            new RaceStartedFaker(faker).Generate();

        public static IEnumerable<RaceStarted> GenerateForever(Faker? faker = null) =>
            RaceStarted.New(faker).GenerateForever();

        public static List<RaceStarted> Generate(int count, Faker? faker = null) =>
            RaceStarted.GenerateForever(faker).Take(count).ToList();
    }
}

public static class LapCompletedExtensions {
    extension(LapCompleted) {
        public static LapCompletedFaker New(Faker? faker = null) => new(faker);

        public static LapCompleted Generate(Faker? faker = null) =>
            new LapCompletedFaker(faker).Generate();

        public static IEnumerable<LapCompleted> GenerateForever(Faker? faker = null) =>
            LapCompleted.New(faker).GenerateForever();

        public static List<LapCompleted> Generate(int count, Faker? faker = null) =>
            LapCompleted.GenerateForever(faker).Take(count).ToList();
    }
}

public static class OvertakePerformedExtensions {
    extension(OvertakePerformed) {
        public static OvertakePerformedFaker New(Faker? faker = null) => new(faker);

        public static OvertakePerformed Generate(Faker? faker = null) =>
            new OvertakePerformedFaker(faker).Generate();

        public static IEnumerable<OvertakePerformed> GenerateForever(Faker? faker = null) =>
            OvertakePerformed.New(faker).GenerateForever();

        public static List<OvertakePerformed> Generate(int count, Faker? faker = null) =>
            OvertakePerformed.GenerateForever(faker).Take(count).ToList();
    }
}

public static class PitStopPerformedExtensions {
    extension(PitStopPerformed) {
        public static PitStopPerformedFaker New(Faker? faker = null) => new(faker);

        public static PitStopPerformed Generate(Faker? faker = null) =>
            new PitStopPerformedFaker(faker).Generate();

        public static IEnumerable<PitStopPerformed> GenerateForever(Faker? faker = null) =>
            PitStopPerformed.New(faker).GenerateForever();

        public static List<PitStopPerformed> Generate(int count, Faker? faker = null) =>
            PitStopPerformed.GenerateForever(faker).Take(count).ToList();
    }
}

public static class IncidentOccurredExtensions {
    extension(IncidentOccurred) {
        public static IncidentOccurredFaker New(Faker? faker = null) => new(faker);

        public static IncidentOccurred Generate(Faker? faker = null) =>
            new IncidentOccurredFaker(faker).Generate();

        public static IEnumerable<IncidentOccurred> GenerateForever(Faker? faker = null) =>
            IncidentOccurred.New(faker).GenerateForever();

        public static List<IncidentOccurred> Generate(int count, Faker? faker = null) =>
            IncidentOccurred.GenerateForever(faker).Take(count).ToList();
    }
}

public static class CarRetiredExtensions {
    extension(CarRetired) {
        public static CarRetiredFaker New(Faker? faker = null) => new(faker);

        public static CarRetired Generate(Faker? faker = null) =>
            new CarRetiredFaker(faker).Generate();

        public static IEnumerable<CarRetired> GenerateForever(Faker? faker = null) =>
            CarRetired.New(faker).GenerateForever();

        public static List<CarRetired> Generate(int count, Faker? faker = null) =>
            CarRetired.GenerateForever(faker).Take(count).ToList();
    }
}

public static class WeatherChangedExtensions {
    extension(WeatherChanged) {
        public static WeatherChangedFaker New(Faker? faker = null) => new(faker);

        public static WeatherChanged Generate(Faker? faker = null) =>
            new WeatherChangedFaker(faker).Generate();

        public static IEnumerable<WeatherChanged> GenerateForever(Faker? faker = null) =>
            WeatherChanged.New(faker).GenerateForever();

        public static List<WeatherChanged> Generate(int count, Faker? faker = null) =>
            WeatherChanged.GenerateForever(faker).Take(count).ToList();
    }
}

public static class RaceFinishedExtensions {
    extension(RaceFinished) {
        public static RaceFinishedFaker New(Faker? faker = null) => new(faker);

        public static RaceFinished Generate(Faker? faker = null) =>
            new RaceFinishedFaker(faker).Generate();

        public static IEnumerable<RaceFinished> GenerateForever(Faker? faker = null) =>
            RaceFinished.New(faker).GenerateForever();

        public static List<RaceFinished> Generate(int count, Faker? faker = null) =>
            RaceFinished.GenerateForever(faker).Take(count).ToList();
    }
}

#endregion
