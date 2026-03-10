using Bogus;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class HomeFaker : Faker<SmartHome> {
    public HomeFaker(int? rooms = null, int? devicesPerRoom = null) {
        CustomInstantiator(f => {
                var homeId    = f.Random.AlphaNumeric(6).ToLower();
                var homeName  = f.Random.ArrayElement([
                    $"{f.Name.LastName()} Family Home",
                    $"{f.Name.LastName()} Residence",
                    $"{f.Address.StreetName()} House",
                    $"{f.Address.StreetName()} Villa",
                    $"{f.Address.City()} Apartment {f.Random.Number(1, 50)}",
                    $"{f.Company.CompanyName()} Executive Home"
                ]);
                var nRooms    = rooms ?? f.Random.Number(3, 8);
                var homeRooms = new RoomFaker(devicesPerRoom, homeId).Generate(nRooms);

                return new SmartHome {
                    Id        = homeId,
                    Name      = homeName,
                    Rooms     = homeRooms,
                    CreatedAt = f.Date.Between(DateTime.UtcNow.AddYears(-10), DateTime.UtcNow.AddMonths(-1))
                };
            }
        );
    }
}
