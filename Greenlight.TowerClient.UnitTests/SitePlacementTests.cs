namespace Greenlight.TowerClient.UnitTests;

/// <summary>
/// Where the site stands. The whole reason this is not simply the bottom-right corner is a
/// maximized editor's scrollbar, and a tower standing on it is the kind of wrong that is only
/// obvious on somebody else's machine at somebody else's scale.
/// </summary>
public class SitePlacementTests
{
    private static readonly PixelBounds Desktop = new(0, 0, 1920, 1040);   // a 1080p screen over a 40px taskbar

    [Fact]
    public void Stands_on_the_taskbar_just_inside_the_scrollbar()
    {
        var inset = SitePlacement.Inset(SiteSide.Right, EdgeRoom.Scrollbar, scrollbar: 17, extra: 0, scaling: 1);
        var site = SitePlacement.Place(Desktop, 240, 280, SiteSide.Right, inset);

        Assert.Equal(17 + 4, inset);
        Assert.Equal(1920 - 21 - 240, site.X);
        Assert.Equal(1040, site.Bottom);            // feet on the taskbar's top edge
        Assert.Equal(240, site.Width);
        Assert.Equal(280, site.Height);
    }

    [Fact]
    public void Leaves_room_for_an_ide_tool_strip_as_well()
    {
        var inset = SitePlacement.Inset(SiteSide.Right, EdgeRoom.ScrollbarAndToolStrip, 17, 0, 1);

        Assert.Equal(17 + 4 + 24, inset);
    }

    [Fact]
    public void Can_go_right_into_the_corner()
    {
        Assert.Equal(4, SitePlacement.Inset(SiteSide.Right, EdgeRoom.None, 17, 0, 1));
    }

    /// <summary>
    /// The scrollbar comes in already in physical pixels at the screen's scale; only the margin
    /// and the tool strip are logical and need scaling. Scaling the scrollbar again would push
    /// the site half a scrollbar too far in at 150%.
    /// </summary>
    [Fact]
    public void Scales_the_margin_but_not_the_scrollbar_twice()
    {
        var inset = SitePlacement.Inset(SiteSide.Right, EdgeRoom.ScrollbarAndToolStrip, scrollbar: 26, extra: 0, scaling: 1.5);

        Assert.Equal(26 + (int)Math.Round((4 + 24) * 1.5), inset);
    }

    [Fact]
    public void Adds_whatever_extra_room_the_file_asks_for()
    {
        Assert.Equal(17 + 4 + 30, SitePlacement.Inset(SiteSide.Right, EdgeRoom.Scrollbar, 17, 30, 1));
    }

    /// <summary>Scrollbars are on the right. On the left there is nothing to keep clear of.</summary>
    [Fact]
    public void On_the_left_keeps_only_its_margin()
    {
        var inset = SitePlacement.Inset(SiteSide.Left, EdgeRoom.ScrollbarAndToolStrip, 17, 0, 1);
        var site = SitePlacement.Place(Desktop, 240, 280, SiteSide.Left, inset);

        Assert.Equal(4, inset);
        Assert.Equal(4, site.X);
        Assert.Equal(1040, site.Bottom);
    }

    /// <summary>A primary monitor that is not at the origin is a perfectly ordinary layout.</summary>
    [Fact]
    public void Works_from_wherever_the_desktop_starts()
    {
        var shifted = new PixelBounds(-1920, 200, 1920, 1040);
        var site = SitePlacement.Place(shifted, 240, 280, SiteSide.Right, 21);

        Assert.Equal(-21 - 240, site.X);
        Assert.Equal(1240, site.Bottom);
    }

    [Fact]
    public void Never_larger_than_the_desktop()
    {
        var tiny = new PixelBounds(0, 0, 200, 150);
        var site = SitePlacement.Place(tiny, 240, 280, SiteSide.Right, 21);

        Assert.True(site.Width <= 200 && site.Height <= 150);
        Assert.True(site.X >= 0 && site.Right <= 200);
    }

    [Fact]
    public void Never_pushed_off_the_far_side_by_a_huge_inset()
    {
        var site = SitePlacement.Place(Desktop, 240, 280, SiteSide.Right, inset: 5000);

        Assert.Equal(0, site.X);
    }

    [Fact]
    public void Nowhere_to_stand_is_nowhere()
    {
        Assert.True(SitePlacement.Place(default, 240, 280, SiteSide.Right, 21).IsEmpty);
    }
}
