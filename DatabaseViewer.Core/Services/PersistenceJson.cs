using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseViewer.Core.Services;

internal static class PersistenceJson
{
    public static readonly JsonSerializerOptions ConnectionStoreOptions = CreateOptions(ignoreNullValues: true);

    public static readonly JsonSerializerOptions ApplicationSettingsOptions = CreateOptions(ignoreNullValues: false);

    private static JsonSerializerOptions CreateOptions(bool ignoreNullValues)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = ignoreNullValues ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
