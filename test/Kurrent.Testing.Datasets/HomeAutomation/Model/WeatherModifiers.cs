namespace Testing.Datasets.HomeAutomation.Model;

public static class WeatherModifiers {
    public static (double TempOffset, double HumidityOffset, double WindowCloseFactor) For(
        WeatherCondition weather
    ) => weather switch {
        WeatherCondition.Sunny  => (+2.0, -5.0,  1.0),
        WeatherCondition.Cloudy => ( 0.0, +3.0,  1.0),
        WeatherCondition.Rainy  => (-2.0, +10.0, 3.0),
        WeatherCondition.Windy  => (-1.0, -3.0,  2.0),
        WeatherCondition.Stormy => (-3.0, +15.0, 5.0),
        _                       => ( 0.0,  0.0,  1.0)
    };
}
