using Bogus;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class HomeFaker : Faker<SmartHome> {
    public HomeFaker() {
        CustomInstantiator(f => {
                // Generate home properties
                var homeId = f.Random.AlphaNumeric(6).ToLower();
                var homeName = f.Random.ArrayElement(
                    [
                        $"{f.Name.LastName()} Family Home",
                        $"{f.Name.LastName()} Residence",
                        $"{f.Address.StreetName()} House",
                        $"{f.Address.StreetName()} Villa",
                        $"{f.Address.City()} Apartment {f.Random.Number(1, 50)}",
                        $"{f.Company.CompanyName()} Executive Home"
                    ]
                );

                var createdAt = f.Date.Between(DateTime.UtcNow.AddYears(-10), DateTime.UtcNow.AddMonths(-1));

                // Generate rooms
                var roomFaker = new RoomFaker();
                var baseRooms = new List<Room> {
                    new() { RoomId = Guid.NewGuid(), Name = "Living Room", Type = RoomType.LivingRoom },
                    new() { RoomId = Guid.NewGuid(), Name = "Kitchen", Type = RoomType.Kitchen },
                    new() { RoomId = Guid.NewGuid(), Name = "Master Bedroom", Type = RoomType.Bedroom }
                };

                var additionalRooms = roomFaker.Generate(f.Random.Number(2, 5));
                baseRooms.AddRange(additionalRooms);

                // Create temporary home to pass to device faker
                var tempHome = new SmartHome {
                    Id        = homeId,
                    Name      = homeName,
                    Rooms     = baseRooms,
                    Devices   = [], // Empty for now
                    CreatedAt = createdAt
                };

                // Generate devices for this home
                var deviceCollectionFaker = new HomeDeviceCollectionFaker(tempHome, 12);
                var devices               = deviceCollectionFaker.Generate();

                // Return complete home with devices
                return new SmartHome {
                    Id        = homeId,
                    Name      = homeName,
                    Rooms     = baseRooms,
                    Devices   = devices,
                    CreatedAt = createdAt
                };
            }
        );
    }
}