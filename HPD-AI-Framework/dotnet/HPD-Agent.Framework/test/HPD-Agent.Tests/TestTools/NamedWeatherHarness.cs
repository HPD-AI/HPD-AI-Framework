namespace HPD.Agent.Tests.TestToolHarnesses;

public class NamedWeatherToolHarness
{
    [AIFunction(Name = "get_weather"), AIDescription("Gets weather for a city")]
    public string GetWeather(string city) => $"Sunny in {city}";

    [AIFunction(Name = "get_forecast"), AIDescription("Gets a weather forecast for a city")]
    public string GetForecast(string city) => $"Forecast for {city}";
}
