using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Greenlight.TowerClient;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelBounds(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>Which end of the taskbar the site is on.</summary>
public enum SiteSide
{
    Right,
    Left,
}

/// <summary>How much room to leave between the site and the right-hand edge of the screen.</summary>
public enum EdgeRoom
{
    /// <summary>Right up against the edge.</summary>
    None,

    /// <summary>
    /// A standard vertical scrollbar's worth, so a maximized editor's scrollbar is never
    /// underneath the tower.
    /// </summary>
    Scrollbar,

    /// <summary>
    /// A scrollbar plus a tool-window strip — the column of buttons an IDE like Rider or Visual
    /// Studio keeps down its right-hand edge, outside the editor's own scrollbar.
    /// </summary>
    ScrollbarAndToolStrip,
}

/// <summary>
/// Where the building site goes: standing on the taskbar, at one end of the screen, clear of
/// the scrollbar of whatever is maximized behind it.
/// </summary>
/// <remarks>
/// <para>
/// "On the taskbar" is the bottom of the work area, not the taskbar's rectangle. The work area
/// already stops where the taskbar starts, and it is also the right answer for a taskbar down
/// one side or set to auto-hide — the site then stands on the bottom of the screen, which is
/// better than standing on nothing.
/// </para>
/// <para>
/// The scrollbar is the reason this is not simply the corner. A maximized IDE puts its
/// vertical scrollbar down the very right-hand edge of the screen, and a tower in the corner
/// would stand on top of the one control that is always being reached for. Moved in by a
/// scrollbar's width, the tower sits just inside it — over the end of the code, which nobody
/// is clicking on, since the site is click-through anyway.
/// </para>
/// </remarks>
public static class SitePlacement
{
    /// <summary>
    /// A tool-window strip, in logical pixels. Rider's and Visual Studio's are both a shade
    /// over 20 at 100%; a couple of pixels of slack is kinder than touching it.
    /// </summary>
    public const double ToolStripWidth = 24;

    /// <summary>A little air between the site and whatever it is keeping clear of.</summary>
    public const double Margin = 4;

    /// <summary>
    /// The site's rectangle, in physical pixels.
    /// </summary>
    /// <param name="workArea">The usable desktop, as Windows reports it.</param>
    /// <param name="width">How wide the site is, physical pixels.</param>
    /// <param name="height">How tall, physical pixels.</param>
    /// <param name="side">Which end.</param>
    /// <param name="inset">How far in from that end, physical pixels.</param>
    public static PixelBounds Place(PixelBounds workArea, int width, int height, SiteSide side, int inset)
    {
        if (workArea.IsEmpty) return default;

        // Never wider or taller than the desktop it is standing on, and never pushed off the far
        // side by an inset somebody typed too much of into the file.
        width = Math.Clamp(width, 1, workArea.Width);
        height = Math.Clamp(height, 1, workArea.Height);
        inset = Math.Clamp(inset, 0, workArea.Width - width);

        var x = side == SiteSide.Right
            ? workArea.Right - inset - width
            : workArea.X + inset;

        return new PixelBounds(x, workArea.Bottom - height, width, height);
    }

    /// <summary>
    /// How far in from the edge to stand, in physical pixels.
    /// </summary>
    /// <param name="side">
    /// Scrollbars are on the right. On the left the site only keeps its margin — nothing lives
    /// down the left-hand edge of a maximized editor that the site could be in the way of.
    /// </param>
    /// <param name="room">How much to leave on the right.</param>
    /// <param name="scrollbar">The system's vertical scrollbar width, physical pixels.</param>
    /// <param name="extra">Anything more the user asked for in the file, logical pixels.</param>
    /// <param name="scaling">Physical pixels per logical pixel.</param>
    public static int Inset(SiteSide side, EdgeRoom room, int scrollbar, double extra, double scaling)
    {
        var logical = Margin + Math.Max(0, extra);

        if (side == SiteSide.Right)
        {
            if (room == EdgeRoom.ScrollbarAndToolStrip) logical += ToolStripWidth;
            if (room != EdgeRoom.None) return scrollbar + (int)Math.Round(logical * scaling);
        }

        return (int)Math.Round(logical * scaling);
    }
}

/// <summary>The two questions <see cref="SitePlacement"/> needs Windows to answer.</summary>
[SupportedOSPlatform("windows")]
internal static class DesktopNative
{
    private const uint SpiGetWorkArea = 0x0030;
    private const int SmCxVScroll = 2;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref Rect pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    /// <summary>The primary monitor's work area: everything but the taskbar.</summary>
    public static PixelBounds WorkArea()
    {
        var work = new Rect();
        if (SystemParametersInfoW(SpiGetWorkArea, 0, ref work, 0) && work.Right > work.Left && work.Bottom > work.Top)
            return new PixelBounds(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);

        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        return width > 0 && height > 0 ? new PixelBounds(0, 0, width, height) : new PixelBounds(0, 0, 1280, 720);
    }

    /// <summary>
    /// The width of a vertical scrollbar at this scale, physical pixels. Asked for at the
    /// window's own DPI: a per-monitor-aware process gets the primary monitor's answer from the
    /// plain call, which is the wrong one on any other scale.
    /// </summary>
    public static int ScrollbarWidth(double scaling)
    {
        try
        {
            var width = GetSystemMetricsForDpi(SmCxVScroll, (uint)Math.Round(96 * scaling));
            if (width > 0) return width;
        }
        catch (EntryPointNotFoundException)
        {
            // Older than Windows 10 1607. Fall through to the unscaled answer.
        }

        // 17 is the classic scrollbar at 100%, for a machine that will not say.
        var plain = GetSystemMetrics(SmCxVScroll);
        return plain > 0 ? plain : (int)Math.Round(17 * Math.Max(1, scaling));
    }
}
