using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace CrystalRadio;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool IsConfigWindowMovable { get; set; } = true;
    public int DefaultSearchLimit { get; set; } = 100;

    public List<string> FavoriteStationIds { get; set; } = new();
    public List<FavoriteStationConfig> FavoriteStations { get; set; } = new();
    public List<CustomStationConfig> CustomStations { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public class CustomStationConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
}

[Serializable]
public class FavoriteStationConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool IsCustom { get; set; }
    public string Source { get; set; } = "Radio Browser";
}
