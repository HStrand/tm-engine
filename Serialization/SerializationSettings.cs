using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace tm_engine.Serialization;

/// <summary>
/// Central factory for shared JsonSerializerSettings used across all endpoints and storage.
/// </summary>
public static class SerializationSettings
{
    public static JsonSerializerSettings Create()
    {
        var resolver = new CamelCasePropertyNamesContractResolver();
        resolver.NamingStrategy!.ProcessDictionaryKeys = false;

        var settings = new JsonSerializerSettings
        {
            ContractResolver = resolver,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        };

        settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        settings.Converters.Add(new MoveJsonConverter());
        settings.Converters.Add(new PendingActionJsonConverter());
        settings.Converters.Add(new VictoryPointsJsonConverter());
        settings.Converters.Add(new HexCoordKeyDictionaryConverter());

        return settings;
    }
}
