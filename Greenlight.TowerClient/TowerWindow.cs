using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Greenlight.TowerClient;

/// <summary>
/// The building site: a transparent, always-on-top, click-through window standing on the
/// taskbar, with the tower and its crew drawn on it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TowerWindow : Window
{
    private readonly TowerConfig _config;
    private readonly TowerCanvas _canvas;
    private readonly DispatcherTimer _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan _lastFrame;
    private TimeSpan _lastHousekeeping;
    private PixelBounds _site;

    public TowerWindow(TowerConfig config, TowerSimulation simulation)
    {
        _config = config;
        Simulation = simulation;

        Title = "Greenlight tower";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Never take the caret out of somebody's editor — which is exactly what this is
        // standing in front of. Paired with WS_EX_NOACTIVATE, which is what makes a click
        // landing here harmless in the first place.
        ShowActivated = false;

        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _canvas = new TowerCanvas(simulation);
        Content = _canvas;
        ApplyLook();

        // 60fps. A desk toy that stutters is worse than no desk toy, and a frame is a few
        // hundred filled rectangles at the very most.
        _frames = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frames.Tick += OnFrame;
    }

    public TowerSimulation Simulation { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Both of these need a real window handle, so neither can run before it is shown.
        ClickThroughNative.Apply(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        LayOutSite();

        _lastFrame = _clock.Elapsed;
        _frames.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frames.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// Take up a setting changed from the tray while the site is running. The config object is
    /// shared with the tray, so there is nothing to pass — this is the site being told to go
    /// and look again.
    /// </summary>
    public void ApplyConfig()
    {
        ApplyLook();
        LayOutSite();
    }

    private void ApplyLook()
    {
        _canvas.Opacity = _config.Opacity;
        _canvas.Mirrored = _config.Side == SiteSide.Left;

        Simulation.Shouting = _config.Screaming;
        Simulation.BrickColors = _config.BrickColors;
        Simulation.SetCrew(_config.Builders);
    }

    /// <summary>Where the site should be right now, physical pixels.</summary>
    private PixelBounds Site()
    {
        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        var inset = SitePlacement.Inset(_config.Side, _config.RoomOnTheRight,
            DesktopNative.ScrollbarWidth(scaling), _config.ExtraInset, scaling);

        return SitePlacement.Place(DesktopNative.WorkArea(),
            (int)Math.Round(TowerConfig.BaseWidth * _config.Size * scaling),
            (int)Math.Round(TowerConfig.BaseHeight * _config.Size * scaling),
            _config.Side, inset);
    }

    /// <summary>
    /// Put the window over the site and tell the simulation how big it is. Re-run when the
    /// taskbar moves or the screen changes.
    /// </summary>
    private void LayOutSite()
    {
        var site = Site();
        if (site.IsEmpty) return;

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        // Position is physical, Width and Height are logical. Mixing those up puts the site at
        // a plausible-looking but wrong size on every scaled display — and on top of the
        // scrollbar it was meant to be keeping clear of.
        Position = new PixelPoint(site.X, site.Y);
        Width = site.Width / scaling;
        Height = site.Height / scaling;

        _site = site;
        Simulation.Resize(Width, Height, TowerConfig.BaseUnit * _config.Size);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastFrame;
        _lastFrame = now;

        // Once a second: re-claim the top of the z-order, and notice if the taskbar or the
        // screen has moved. Cheap, and much less code than listening for every way Windows has
        // of mentioning either.
        if (now - _lastHousekeeping >= TimeSpan.FromSeconds(1))
        {
            _lastHousekeeping = now;

            ClickThroughNative.KeepOnTop(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
            if (Site() != _site) LayOutSite();
        }

        Simulation.Advance(elapsed);
        _canvas.InvalidateVisual();
    }
}
