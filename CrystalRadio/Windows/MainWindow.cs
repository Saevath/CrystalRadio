using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using CrystalRadio.Services;

namespace CrystalRadio.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly IRadioService radioService;

    private string searchQuery = string.Empty;
    private string previousSearchQuery = string.Empty;
    private bool stationsLoaded;
    private string loadingMessage = "Loading stations...";
    private List<RadioStation> displayedStations = new();
    private bool isSearching;
    private DateTime lastSearchTime = DateTime.MinValue;
    private const int SearchDebounceMs = 500;

    private readonly Dictionary<string, IDalamudTextureWrap?> imageCache = new();

    private string favoriteSearchQuery = string.Empty;
    private string previousFavoriteSearchQuery = string.Empty;
    private List<RadioStation> displayedFavorites = new();
    private bool isSearchingFavorites;
    private DateTime lastFavoriteSearchTime = DateTime.MinValue;

    private readonly string[] eqLabels =
    {
        "Bass (64 Hz)",
        "Low Mid (250 Hz)",
        "Mid (1 kHz)",
        "High Mid (4 kHz)",
        "Treble (12 kHz)"
    };

    private float[] eqGains = { 0f, 0f, 0f, 0f, 0f };

    public MainWindow(Plugin plugin, IRadioService radioService)
        : base("Crystal Radio##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        this.radioService = radioService;

        var gains = plugin.GetEqGains();
        if (gains.Length == eqGains.Length)
            eqGains = gains;

        LoadStations();
    }

    private async void LoadStations()
    {
        try
        {
            await radioService.LoadStationsAsync();

            displayedStations = radioService.Stations
                .OrderByDescending(s => s.IsFavorite)
                .ThenBy(s => s.Name)
                .ToList();

            displayedFavorites = radioService.FavoriteStations
                .OrderBy(s => s.Name)
                .Take(100)
                .ToList();

            stationsLoaded = true;
        }
        catch (Exception ex)
        {
            loadingMessage = $"Error loading stations: {ex.Message}";
        }
    }

    public void Dispose()
    {
        foreach (var texture in imageCache.Values.OfType<IDalamudTextureWrap>())
            texture?.Dispose();

        imageCache.Clear();
    }

    public override void Draw()
    {
        if (!stationsLoaded)
        {
            ImGui.TextUnformatted(loadingMessage);
            return;
        }

        DrawPlaybackControls();
        ImGui.Spacing();
        DrawEqControls();
        ImGui.Spacing();
        DrawVolumeControl();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawStationList();
    }

    private void DrawPlaybackControls()
    {
        var currentStation = radioService.CurrentStation;
        var isPlaying = radioService.CurrentState == PlaybackState.Playing;
        var isPaused = radioService.CurrentState == PlaybackState.Paused;

        ImGui.TextUnformatted("Now Playing:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0, 1, 0, 1), currentStation?.Name ?? "Nothing");

        if (currentStation?.CurrentTrack != null)
        {
            ImGui.TextUnformatted("Track:");
            ImGui.SameLine();
            ImGui.TextColored(
                new Vector4(0.7f, 0.7f, 1f, 1f),
                string.IsNullOrWhiteSpace(currentStation.CurrentTrack) ? "Unknown Track" : currentStation.CurrentTrack);
        }

        ImGui.Spacing();

        if (ImGui.Button("Play", new Vector2(100, 0)) && currentStation != null && !isPlaying)
        {
            if (isPaused)
                radioService.Resume();
            else
                _ = radioService.PlayStationAsync(currentStation);
        }

        ImGui.SameLine();

        if (ImGui.Button("Pause", new Vector2(100, 0)) && isPlaying)
            radioService.Pause();

        ImGui.SameLine();

        if (ImGui.Button("Stop", new Vector2(100, 0)) && radioService.CurrentState != PlaybackState.Stopped)
            radioService.Stop();

        DrawCurrentStationIcon();
    }

    private void DrawEqControls()
    {
        ImGui.TextUnformatted("EQ");

        for (var i = 0; i < eqLabels.Length; i++)
        {
            var gain = eqGains[i];
            if (ImGui.SliderFloat($"##eq_{i}", ref gain, -12f, 12f, $"{eqLabels[i]}: %.1f dB"))
            {
                eqGains[i] = gain;
                plugin.SetEqGain(i, gain);
            }
        }

        if (ImGui.Button("Reset EQ"))
        {
            plugin.ResetEq();

            var gains = plugin.GetEqGains();
            if (gains.Length == eqGains.Length)
                eqGains = gains;
        }
    }

    private void DrawCurrentStationIcon()
    {
        var currentStation = radioService.CurrentStation;
        if (currentStation == null || string.IsNullOrWhiteSpace(currentStation.IconUrl))
            return;

        if (!imageCache.ContainsKey(currentStation.IconUrl))
        {
            LoadImageForStation(currentStation);
            return;
        }

        if (imageCache.TryGetValue(currentStation.IconUrl, out var texture) && texture != null)
        {
            ImGui.SameLine();
            ImGui.Image(texture.Handle, new Vector2(64, 64));
        }
    }

    private void DrawVolumeControl()
    {
        var volume = radioService.Volume * 100f;

        ImGui.TextUnformatted("Volume:");
        ImGui.SameLine();

        if (ImGui.SliderFloat("##Volume", ref volume, 0f, 100f, "%.0f%%"))
            radioService.Volume = volume / 100f;
    }

    private void DrawStationList()
    {
        using var tabBar = ImRaii.TabBar("StationTabs");
        if (!tabBar.Success)
            return;

        using (var stationsTab = ImRaii.TabItem("Stations"))
        {
            if (stationsTab.Success)
                DrawStationsTab();
        }

        using (var favoritesTab = ImRaii.TabItem("Favorites"))
        {
            if (favoritesTab.Success)
                DrawFavoritesTab();
        }
    }

    private void DrawStationsTab()
    {
        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        ImGui.TextUnformatted("Search:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);

        if (ImGui.InputText("##SearchStations", ref searchQuery, 256))
        {
            if (searchQuery != previousSearchQuery)
                lastSearchTime = DateTime.UtcNow;
        }

        if ((DateTime.UtcNow - lastSearchTime).TotalMilliseconds >= SearchDebounceMs &&
            searchQuery != previousSearchQuery &&
            !isSearching)
        {
            previousSearchQuery = searchQuery;
            PerformAsyncSearch(searchQuery);
        }

        ImGui.Spacing();

        var stationCount = displayedStations.Count;
        var statusText = isSearching
            ? "Available Stations (Searching...)"
            : $"Available Stations ({stationCount})";

        ImGui.TextUnformatted(statusText);

        using var child = ImRaii.Child("StationListChild", Vector2.Zero, true);
        if (!child.Success)
            return;

        foreach (var station in displayedStations)
            DrawStationItem(station, false);
    }

    private void DrawFavoritesTab()
    {
        ImGui.TextUnformatted("Search:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);

        if (ImGui.InputText("##SearchFavorites", ref favoriteSearchQuery, 256))
        {
            if (favoriteSearchQuery != previousFavoriteSearchQuery)
                lastFavoriteSearchTime = DateTime.UtcNow;
        }

        if ((DateTime.UtcNow - lastFavoriteSearchTime).TotalMilliseconds >= SearchDebounceMs &&
            favoriteSearchQuery != previousFavoriteSearchQuery &&
            !isSearchingFavorites)
        {
            previousFavoriteSearchQuery = favoriteSearchQuery;
            PerformAsyncFavoriteSearch(favoriteSearchQuery);
        }

        ImGui.Spacing();

        var favoriteCount = displayedFavorites.Count;
        var statusText = isSearchingFavorites
            ? "Favorite Stations (Searching...)"
            : $"Favorite Stations ({favoriteCount})";

        ImGui.TextUnformatted(statusText);

        using var child = ImRaii.Child("FavoriteListChild", Vector2.Zero, true);
        if (!child.Success)
            return;

        foreach (var station in displayedFavorites)
            DrawStationItem(station, true);
    }

    private void PerformAsyncFavoriteSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            displayedFavorites = radioService.FavoriteStations.Take(100).ToList();
            isSearchingFavorites = false;
            return;
        }

        isSearchingFavorites = true;

        try
        {
            var lowerQuery = query.ToLowerInvariant();

            displayedFavorites = radioService.FavoriteStations
                .Where(s =>
                    s.Name.ToLowerInvariant().Contains(lowerQuery) ||
                    s.Genre.ToLowerInvariant().Contains(lowerQuery) ||
                    s.Country.ToLowerInvariant().Contains(lowerQuery))
                .Take(100)
                .ToList();
        }
        catch
        {
            displayedFavorites = new List<RadioStation>();
        }
        finally
        {
            isSearchingFavorites = false;
        }
    }

    private async void PerformAsyncSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            displayedStations = radioService.Stations
                .OrderByDescending(s => s.IsFavorite)
                .ThenBy(s => s.Name)
                .ToList();

            isSearching = false;
            return;
        }

        isSearching = true;

        try
        {
            var results = await radioService.SearchStationsAsync(query, plugin.Configuration.DefaultSearchLimit);
            displayedStations = results;
        }
        catch
        {
            displayedStations = new List<RadioStation>();
        }
        finally
        {
            isSearching = false;
        }
    }

    private void DrawStationItem(RadioStation station, bool inFavoritesTab)
    {
        var isCurrentStation = radioService.CurrentStation?.Id == station.Id;
        var isFavorite = station.IsFavorite;

        ImGui.PushID(station.Id);

        if (isCurrentStation)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.2f, 1f));

        var starButtonWidth = 40f;
        var stationButtonWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - starButtonWidth - ImGui.GetStyle().ItemSpacing.X);

        var stationLabel = $"{(isFavorite ? "★" : " ")} {station.Name}";
        if (ImGui.Button(stationLabel, new Vector2(stationButtonWidth, 0)))
            _ = radioService.PlayStationAsync(station);

        var isStationHovered = ImGui.IsItemHovered();

        if (isCurrentStation)
            ImGui.PopStyleColor();

        ImGui.SameLine();

        if (isFavorite)
        {
            if (ImGui.Button("★##favorite", new Vector2(starButtonWidth, 0)))
            {
                radioService.RemoveFavorite(station);
                RefreshFavoriteDisplay();

                if (inFavoritesTab)
                {
                    if (!string.IsNullOrWhiteSpace(favoriteSearchQuery))
                        PerformAsyncFavoriteSearch(favoriteSearchQuery);
                }
                else
                {
                    RefreshStationDisplayIfNeeded();
                }
            }
        }
        else
        {
            if (ImGui.Button("☆##favorite", new Vector2(starButtonWidth, 0)))
            {
                radioService.AddFavorite(station);
                RefreshFavoriteDisplay();

                if (inFavoritesTab)
                {
                    if (!string.IsNullOrWhiteSpace(favoriteSearchQuery))
                        PerformAsyncFavoriteSearch(favoriteSearchQuery);
                }
                else
                {
                    RefreshStationDisplayIfNeeded();
                }
            }
        }

        // Tooltip with detailed info
        if (isStationHovered)
        {
            ImGui.BeginTooltip();

            ImGui.TextUnformatted(station.Name);

            if (!string.IsNullOrWhiteSpace(station.Source))
                ImGui.TextDisabled($"Source: {station.Source}");

            if (!string.IsNullOrWhiteSpace(station.Genre))
                ImGui.TextDisabled($"Genre: {station.Genre}");

            if (!string.IsNullOrWhiteSpace(station.Country) || !string.IsNullOrWhiteSpace(station.Language))
            {
                var location = $"{station.Country}";
                if (!string.IsNullOrWhiteSpace(station.Language))
                    location += $" - {station.Language}";

                ImGui.TextDisabled(location);
            }

            if (!string.IsNullOrWhiteSpace(station.Description))
            {
                ImGui.Separator();
                ImGui.TextWrapped(station.Description);
            }

            ImGui.EndTooltip();
        }

        ImGui.PopID();
    }

    private void RefreshFavoriteDisplay()
    {
        if (string.IsNullOrWhiteSpace(favoriteSearchQuery))
        {
            displayedFavorites = radioService.FavoriteStations
                .OrderBy(s => s.Name)
                .Take(100)
                .ToList();
        }
        else
        {
            PerformAsyncFavoriteSearch(favoriteSearchQuery);
        }
    }

    private void RefreshStationDisplayIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            displayedStations = radioService.Stations
                .OrderByDescending(s => s.IsFavorite)
                .ThenBy(s => s.Name)
                .ToList();
        }
    }

    private void LoadImageForStation(RadioStation station)
    {
        if (string.IsNullOrEmpty(station.IconUrl))
            return;

        if (!imageCache.ContainsKey(station.IconUrl))
            LoadImageAsyncForStation(station);
    }

    private async void LoadImageAsyncForStation(RadioStation station)
    {
        if (string.IsNullOrEmpty(station.IconUrl))
            return;

        try
        {
            using var httpClient = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var imageData = await httpClient.GetByteArrayAsync(station.IconUrl);

            if (imageData == null || imageData.Length == 0)
            {
                imageCache[station.IconUrl] = null;
                return;
            }

            var texture = await Plugin.TextureProvider.CreateFromImageAsync(
                new ReadOnlyMemory<byte>(imageData),
                $"CrystalRadio_{station.Id}");

            imageCache[station.IconUrl] = texture;
        }
        catch
        {
            imageCache[station.IconUrl] = null;
        }
    }
}



