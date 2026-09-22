using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenlight.TowerClient;

/// <summary>
/// The building site, read from a JSON file the user can edit. Written out with the defaults
/// the first time it is missing, so "where do I change the brick colours" has an answer that
/// does not involve rebuilding anything.
/// </summary>
/// <remarks>
/// Kept in AppData rather than beside the executable: the executable lives under <c>bin</c>,
/// which a rebuild is entitled to delete.
/// </remarks>
public sealed class TowerConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.Tower", "tower.json");

    /// <summary>
    /// Which end of the taskbar the site stands on. The right by default — the end nearest the
    /// clock, and the end of a maximized editor where the code runs out.
    /// </summary>
    public SiteSide Side { get; set; } = SiteSide.Right;

    /// <summary>
    /// How much room to leave on the right for whatever is maximized behind the site. A
    /// scrollbar's worth by default, so the tower stands just inside it rather than on it.
    /// </summary>
    public EdgeRoom RoomOnTheRight { get; set; } = EdgeRoom.Scrollbar;

    /// <summary>
    /// More room still, in logical pixels, for an editor with something else down its edge —
    /// a minimap, a wider strip. Added to whatever <see cref="RoomOnTheRight"/> leaves.
    /// </summary>
    public double ExtraInset { get; set; }

    /// <summary>
    /// How big the whole site is. 1 is a builder about fourteen pixels tall on a tower about
    /// two hundred and fifty tall; everything scales together.
    /// </summary>
    public double Size { get; set; } = 1.0;

    /// <summary>How many are on the crew.</summary>
    public int Builders { get; set; } = 5;

    /// <summary>Overall opacity, for when the site is livelier than you want it to be.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Whether anybody says anything — the screaming, mostly. Off, they still run around; they
    /// just do it without the speech bubbles.
    /// </summary>
    public bool Screaming { get; set; } = true;

    /// <summary>
    /// The colours bricks are made in, picked at random per brick. Sandstone by default rather
    /// than red brick, so that a tower standing there on a green build is not also, from a
    /// distance, a red thing on the taskbar.
    /// </summary>
    public List<string> BrickColors { get; set; } = ["#C9A66B", "#B8925A", "#D8BC84", "#A9824E"];

    /// <summary>The site's size at <see cref="Size"/> 1, logical pixels.</summary>
    public const double BaseWidth = 240;

    /// <inheritdoc cref="BaseWidth"/>
    public const double BaseHeight = 280;

    /// <inheritdoc cref="BaseWidth"/>
    public const double BaseUnit = 14;

    /// <summary>
    /// Load the file, writing the defaults out first if it is not there. A file that cannot be
    /// read or parsed falls back to the defaults rather than refusing to start: a stray comma
    /// should not cost anybody the whole site.
    /// </summary>
    public static TowerConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                var fresh = new TowerConfig();
                fresh.Save(file);
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<TowerConfig>(File.ReadAllText(file), Json);
            if (loaded is null) return new TowerConfig();

            // A hand edit that deleted the list, or emptied it, still gets bricks.
            loaded.BrickColors = loaded.BrickColors?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? [];
            if (loaded.BrickColors.Count == 0) loaded.BrickColors = new TowerConfig().BrickColors;

            loaded.Size = Math.Clamp(loaded.Size, 0.5, 2.5);
            loaded.Builders = Math.Clamp(loaded.Builders, 1, 12);
            loaded.Opacity = Math.Clamp(loaded.Opacity, 0.2, 1.0);
            loaded.ExtraInset = Math.Clamp(loaded.ExtraInset, 0, 600);
            return loaded;
        }
        catch
        {
            return new TowerConfig();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // A site that cannot write its config still runs perfectly well on the defaults.
        }
    }

    /// <summary>Take on everything from a freshly-read file, in place.</summary>
    /// <remarks>
    /// Copied into this instance rather than swapping it for the new one: the tray is holding
    /// this object, and it is the tray's menu that has to keep agreeing with the file.
    /// </remarks>
    public void CopyFrom(TowerConfig other)
    {
        Side = other.Side;
        RoomOnTheRight = other.RoomOnTheRight;
        ExtraInset = other.ExtraInset;
        Size = other.Size;
        Builders = other.Builders;
        Opacity = other.Opacity;
        Screaming = other.Screaming;
        BrickColors = other.BrickColors;
    }
}
