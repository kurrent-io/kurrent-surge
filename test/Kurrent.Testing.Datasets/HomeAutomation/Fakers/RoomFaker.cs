using Bogus;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class RoomFaker : Faker<Room> {
    public RoomFaker() {
        CustomInstantiator(f => {
                var roomType = f.PickRandom<RoomType>();
                var roomName = GenerateRoomName(f, roomType);
                return new Room(Guid.NewGuid(), roomName, roomType);
            }
        );
    }

    static string GenerateRoomName(Faker faker, RoomType roomType) =>
        roomType switch {
            RoomType.LivingRoom => faker.PickRandom(
                "Living Room", "Family Room", "Great Room",
                "Lounge", "Front Room"
            ),
            RoomType.Kitchen => faker.PickRandom(
                "Kitchen", "Main Kitchen", "Galley Kitchen",
                "Chef's Kitchen"
            ),
            RoomType.Bedroom => faker.PickRandom(
                "Master Bedroom", "Guest Bedroom", "Kids' Room",
                "Bedroom", "Second Bedroom", "Third Bedroom",
                "Children's Room"
            ),
            RoomType.Bathroom => faker.PickRandom(
                "Main Bathroom", "Master Bathroom", "Guest Bathroom",
                "Half Bath", "Powder Room", "Full Bath", "Ensuite"
            ),
            RoomType.Office => faker.PickRandom(
                "Office", "Study", "Home Office",
                "Den", "Library", "Work Room"
            ),
            RoomType.Garage => faker.PickRandom(
                "Garage", "Two-Car Garage", "Single Garage",
                "Carport", "Workshop"
            ),
            RoomType.Hallway => faker.PickRandom(
                "Hallway", "Main Hall", "Corridor",
                "Foyer", "Entry Hall"
            ),
            RoomType.DiningRoom => faker.PickRandom(
                "Dining Room", "Formal Dining", "Breakfast Nook",
                "Eating Area"
            ),
            RoomType.Basement => faker.PickRandom(
                "Basement", "Lower Level", "Finished Basement",
                "Rec Room", "Wine Cellar"
            ),
            RoomType.Entrance => faker.PickRandom(
                "Entrance", "Front Entry", "Foyer",
                "Vestibule", "Mudroom"
            ),
            RoomType.Utility => faker.PickRandom(
                "Utility Room", "Laundry Room", "Storage Room",
                "Pantry", "Closet"
            ),
            _ => "Room"
        };
}