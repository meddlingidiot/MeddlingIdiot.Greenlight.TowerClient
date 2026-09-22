namespace Greenlight.TowerClient.UnitTests;

/// <summary>
/// The file. Hand edits are the whole point of it, so a bad one has to cost the user the edit,
/// never the site.
/// </summary>
public sealed class TowerConfigTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tower-config-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "tower.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Writes_the_defaults_the_first_time()
    {
        var config = TowerConfig.Load(File);

        Assert.True(System.IO.File.Exists(File));
        Assert.Equal(SiteSide.Right, config.Side);
        Assert.Equal(EdgeRoom.Scrollbar, config.RoomOnTheRight);
        Assert.True(config.Screaming);
        Assert.NotEmpty(config.BrickColors);
    }

    [Fact]
    public void Reads_back_what_it_wrote()
    {
        var config = new TowerConfig
        {
            Side = SiteSide.Left,
            RoomOnTheRight = EdgeRoom.ScrollbarAndToolStrip,
            Builders = 8,
            Screaming = false,
            BrickColors = ["#FF0000"],
        };
        config.Save(File);

        var loaded = TowerConfig.Load(File);

        Assert.Equal(SiteSide.Left, loaded.Side);
        Assert.Equal(EdgeRoom.ScrollbarAndToolStrip, loaded.RoomOnTheRight);
        Assert.Equal(8, loaded.Builders);
        Assert.False(loaded.Screaming);
        Assert.Equal(["#FF0000"], loaded.BrickColors);
    }

    [Fact]
    public void A_file_that_will_not_parse_gives_the_defaults()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, "{ \"Builders\": 5,, }");

        var config = TowerConfig.Load(File);

        Assert.Equal(5, config.Builders);
        Assert.Equal(SiteSide.Right, config.Side);
    }

    [Fact]
    public void Silly_numbers_are_clamped()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, """{ "Builders": 500, "Size": -3, "Opacity": 9, "ExtraInset": -40 }""");

        var config = TowerConfig.Load(File);

        Assert.Equal(12, config.Builders);
        Assert.Equal(0.5, config.Size);
        Assert.Equal(1.0, config.Opacity);
        Assert.Equal(0, config.ExtraInset);
    }

    [Fact]
    public void An_emptied_colour_list_still_gets_bricks()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, """{ "BrickColors": [ "", "  " ] }""");

        Assert.NotEmpty(TowerConfig.Load(File).BrickColors);
    }
}
