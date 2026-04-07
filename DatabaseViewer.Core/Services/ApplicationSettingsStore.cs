using System.IO;
using System.Text.Json;
using DatabaseViewer.Core.Models;

namespace DatabaseViewer.Core.Services;

public sealed class ApplicationSettingsStore
{
    private readonly string _filePath;

    public ApplicationSettingsStore()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DatabaseViewer");

        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "settings.json");
    }

    public async Task<ApplicationSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new ApplicationSettings();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream) ?? new ApplicationSettings();
    }

    public async Task SaveAsync(ApplicationSettings settings)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}