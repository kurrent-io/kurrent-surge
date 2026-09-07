using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Catalogs.Generators.Tests.Samples.SmartHome;
using Kurrent.Surge.Schema.Catalogs.Generators.Tests.Samples.SmartHome.Security;

// Declaration form 1 — group scan: every event type under the Climate namespace joins the "Climate"
// catalog, named by the default convention.
[assembly: MessageCatalog("Kurrent.Surge.Schema.Catalogs.Generators.Tests.Samples.SmartHome.Climate", Key = "Climate")]

// Declaration form 2 — per-type with a pinned wire name; lands in the Default catalog.
[assembly: MessageType<DevicePaired>("smart-home.device-paired.v1")]

namespace Kurrent.Surge.Schema.Catalogs.Generators.Tests.Samples;

/// <summary>
/// Declaration form 3 — a hand-written catalog (<see cref="CustomMessageCatalogAttribute"/>) for the
/// security events, with pinned wire names. The generator instantiates it and exposes it as
/// <c>MessageCatalogs.Security</c>.
/// </summary>
[CustomMessageCatalog]
sealed class SecurityCatalog() : MessageCatalog("Security", c => c
    .Map<MotionDetected>("smart-home.security.motion-detected.v1")
    .Map<DoorLocked>("smart-home.security.door-locked.v1")
    .Map<DoorUnlocked>("smart-home.security.door-unlocked.v1"));
