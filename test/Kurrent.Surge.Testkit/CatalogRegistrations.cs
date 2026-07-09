using Kurrent.Schema.Catalogs;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

// Exports the testkit's shared test contracts through the message catalog GENERATOR — the
// same path consumers use. The types live in Kurrent.Testkit (which has no Kurrent.Surge.Client
// reference), so the assembly-level "types you don't own" form applies. No pins: schema
// names come from the default convention (full type name), matching what tests always used.
[assembly: MessageType<RaceCar>(Catalog = "Testkit")]
[assembly: MessageType<GridFormed>(Catalog = "Testkit")]
[assembly: MessageType<RaceStarted>(Catalog = "Testkit")]
[assembly: MessageType<LapCompleted>(Catalog = "Testkit")]
[assembly: MessageType<OvertakePerformed>(Catalog = "Testkit")]
[assembly: MessageType<PitStopPerformed>(Catalog = "Testkit")]
[assembly: MessageType<IncidentOccurred>(Catalog = "Testkit")]
[assembly: MessageType<CarRetired>(Catalog = "Testkit")]
[assembly: MessageType<WeatherChanged>(Catalog = "Testkit")]
[assembly: MessageType<RaceFinished>(Catalog = "Testkit")]
[assembly: MessageType<RaceInsight>(Catalog = "Testkit")]
