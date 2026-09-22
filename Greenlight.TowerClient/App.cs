using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.TowerClient;

/// <summary>
/// The whole of the Greenlight integration, which is the point of the sample: attach,
/// translate the colour, and never care whether Greenlight is actually there.
/// </summary>
/// <remarks>
/// The tray icon and the start/stop plumbing around it are ordinary Avalonia and have nothing
/// to do with Greenlight — the integration is still the twenty-odd lines in
/// <see cref="StartWatchingGreenlight"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class App : Application
{
    private GreenlightClient? _greenlight;
    private TowerWindow? _window;
    private TowerTray? _tray;
    private TowerConfig _config = new();

    /// <summary>
    /// The site outlives its window. Packing it away from the tray and putting it back up again
    /// should find the tower as it was left — half built, or in pieces — rather than a fresh
    /// empty plot, which would make the tray's off switch a way of cheating the build.
    /// </summary>
    private TowerSimulation? _site;

    private LightState _light = LightState.Unknown;
    private bool _building;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The site's window is closed and reopened by the tray's toggle, and there is no
            // other window — on the default setting, packing the site away would quit the whole
            // thing and take the tray icon with it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = TowerConfig.Load();

            // If Windows is set to start this, make sure it is still pointed at the right
            // executable. An update moves the versioned copy out from under an older
            // registration, and the symptom is the tower silently not coming back one morning.
            WindowsStartup.Refresh();

            _tray = new TowerTray(_config)
            {
                IsRunning = () => _window is not null,
                OnSetRunning = running =>
                {
                    if (running) PutUpSite();
                    else PackAwaySite();
                },
                OnConfigChanged = () => _window?.ApplyConfig(),
                OnReloadConfig = ReloadConfig,
                OnQuit = () => desktop.Shutdown(),
            };

            PutUpSite();

            // --demo runs the whole story on a loop without a Greenlight: build, sway, fall,
            // scream, tidy up, build again. For the screenshots, and for anybody who would like
            // to see it fall down without having to break something first.
            if (desktop.Args?.Contains("--demo", StringComparer.OrdinalIgnoreCase) is true) RunDemo();
            else StartWatchingGreenlight();

            desktop.Exit += async (_, _) =>
            {
                _tray?.Dispose();
                if (_greenlight is not null) await _greenlight.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingGreenlight()
    {
        _greenlight = new GreenlightClient();

        // Both of these arrive on a background thread — the SDK says so, loudly, and this is
        // what it means in practice. Touching the simulation from the pipe's thread would be a
        // race against the render loop reading the same bricks.
        _greenlight.Changed += (_, e) => Apply(Translate(e.Snapshot.Status), e.Snapshot.IsBuilding);
        _greenlight.AvailabilityChanged += (_, e) =>
        {
            // Anything other than Connected means there is nothing to show, and downing tools
            // is more honest than building on stale data.
            if (e.Availability != GreenlightAvailability.Connected) Apply(LightState.Unknown, building: false);
        };

        // Deliberately not awaited and deliberately not guarded: StartAsync returns as soon as
        // the background loop is running, and an absent Greenlight is not an error. The crew
        // stands about until one turns up, then gets to work on its own.
        _ = _greenlight.StartAsync();
    }

    /// <summary>
    /// Green long enough to get somewhere, a spell of yellow, then red for long enough to watch
    /// the screaming — round and round. Hurried throughout, so a lap takes a minute or two
    /// rather than the several a real build would.
    /// </summary>
    private void RunDemo()
    {
        (LightState Light, double Seconds)[] story =
        [
            (LightState.Green, 45),
            (LightState.Yellow, 8),
            (LightState.Red, 12),
        ];

        var chapter = 0;
        Apply(story[0].Light, building: true);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(story[0].Seconds) };
        timer.Tick += (_, _) =>
        {
            chapter = (chapter + 1) % story.Length;
            timer.Interval = TimeSpan.FromSeconds(story[chapter].Seconds);
            Apply(story[chapter].Light, building: true);
        };
        timer.Start();
    }

    private void PutUpSite()
    {
        if (_window is not null) return;

        _site ??= new TowerSimulation(_config.Builders);
        _site.Light = _light;
        _site.Hurry = _building;

        _window = new TowerWindow(_config, _site);
        _window.Show();
        _tray?.ShowLight(_light);
    }

    private void PackAwaySite()
    {
        _window?.Close();
        _window = null;
        _tray?.ShowLight(_light);
    }

    /// <summary>Re-read the file, for colours and sizes edited by hand while this was running.</summary>
    private void ReloadConfig()
    {
        _config.CopyFrom(TowerConfig.Load());
        _window?.ApplyConfig();
    }

    private static LightState Translate(GreenlightStatus status) => status switch
    {
        GreenlightStatus.Green => LightState.Green,
        GreenlightStatus.Yellow => LightState.Yellow,
        GreenlightStatus.Red => LightState.Red,
        _ => LightState.Unknown,
    };

    private void Apply(LightState light, bool building) =>
        Dispatcher.UIThread.Post(() =>
        {
            _light = light;
            _building = building;

            if (_site is not null)
            {
                _site.Light = light;
                _site.Hurry = building;
            }

            _tray?.ShowLight(light);
        });
}
