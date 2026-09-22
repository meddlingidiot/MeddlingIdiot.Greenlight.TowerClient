using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Greenlight.TowerClient;

/// <summary>
/// The mascot in the notification area, and the menu hanging off him: the only part of this
/// toy a person can actually click.
/// </summary>
/// <remarks>
/// The site itself is click-through by design — it stands in front of somebody's editor all
/// day, and a window there that caught clicks would be a window in the way. So every setting in
/// <see cref="TowerConfig"/> that the running site can absorb is reachable from here, and each
/// change is written straight back to the file, so the menu and the JSON are always the same
/// settings.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class TowerTray : IDisposable
{
    private static readonly Uri IconUri = new("avares://Greenlight.TowerClient/Assets/MeddlingIdiot.ico");

    private readonly TowerConfig _config;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _status;
    private readonly NativeMenuItem _running;
    private readonly NativeMenuItem _startup;
    private readonly NativeMenuItem _scream;

    public TowerTray(TowerConfig config)
    {
        _config = config;

        _status = new NativeMenuItem { Header = "Waiting for Greenlight…", IsEnabled = false };
        _running = new NativeMenuItem
        {
            Header = "Tower on the taskbar",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = true,
        };
        _running.Click += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);

        // Read from the registry rather than from a setting of ours, every time it is shown: the
        // user can turn this off in Task Manager's Startup tab, and a tick remembering what we
        // last wrote would then be telling them the opposite of the truth.
        _startup = Check("Start with Windows", WindowsStartup.IsEnabled, value => WindowsStartup.Set(value));
        _scream = Check("Let them scream", () => _config.Screaming, value => Change(() => _config.Screaming = value));

        var menu = BuildMenu();

        // The submenus re-tick their own items when they open. These two are top-level, and both
        // can change behind the menu's back — Task Manager for one, a reloaded file for the other.
        menu.Opening += (_, _) =>
        {
            _startup.IsChecked = WindowsStartup.IsEnabled();
            _scream.IsChecked = _config.Screaming;
        };

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = "Greenlight tower",
            Menu = menu,
            IsVisible = true,
        };

        // The one thing a left click can mean here. There is no main window to open, and a
        // tray icon that does nothing at all when clicked reads as a hung one.
        _tray.Clicked += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);
    }

    /// <summary>Whether the site is currently up.</summary>
    public Func<bool>? IsRunning { get; set; }

    /// <summary>Put the site up, or take it down.</summary>
    public Action<bool>? OnSetRunning { get; set; }

    /// <summary>A setting changed that the running site can absorb.</summary>
    public Action? OnConfigChanged { get; set; }

    /// <summary>Re-read the config file, for edits made by hand while this was running.</summary>
    public Action? OnReloadConfig { get; set; }

    public Action? OnQuit { get; set; }

    /// <summary>Say what the site is doing, in the tooltip and at the top of the menu.</summary>
    public void ShowLight(LightState light)
    {
        var running = IsRunning?.Invoke() ?? true;

        _status.Header = light switch
        {
            LightState.Green => "Greenlight: green — building",
            LightState.Yellow => "Greenlight: yellow — it's swaying",
            LightState.Red => "Greenlight: red — it fell down",
            _ => "Greenlight not running — tools down",
        };

        _running.IsChecked = running;
        _tray.ToolTipText = running ? $"Greenlight tower — {Short(light)}" : "Greenlight tower — packed away";
    }

    private static string Short(LightState light) => light switch
    {
        LightState.Green => "building",
        LightState.Yellow => "swaying",
        LightState.Red => "fallen down",
        _ => "not connected",
    };

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }

    private void SetRunning(bool running)
    {
        OnSetRunning?.Invoke(running);
        _running.IsChecked = running;
        _tray.ToolTipText = running ? "Greenlight tower" : "Greenlight tower — packed away";
    }

    private NativeMenu BuildMenu() =>
    [
        _status,
        new NativeMenuItemSeparator(),
        _running,
        Submenu("Which end",
            Choice("Right", () => _config.Side == SiteSide.Right, () => Change(() => _config.Side = SiteSide.Right)),
            Choice("Left", () => _config.Side == SiteSide.Left, () => Change(() => _config.Side = SiteSide.Left))),
        Submenu("Room on the right",
            Room("None — right in the corner", EdgeRoom.None),
            Room("A scrollbar", EdgeRoom.Scrollbar),
            Room("A scrollbar and a tool strip", EdgeRoom.ScrollbarAndToolStrip)),
        Submenu("How big",
            Size("Small", 0.75),
            Size("Medium", 1.0),
            Size("Large", 1.4)),
        Submenu("How many builders",
            Crew("Three", 3),
            Crew("Five", 5),
            Crew("Eight", 8)),
        Submenu("How solid",
            Opacity("Solid", 1.0),
            Opacity("Nearly solid", 0.8),
            Opacity("Half there", 0.5),
            Opacity("Barely there", 0.3)),
        _scream,
        new NativeMenuItemSeparator(),
        Item("Edit the colours…", EditConfig),
        Item("Reload the file", () => OnReloadConfig?.Invoke()),
        _startup,
        new NativeMenuItemSeparator(),
        Item("Quit", () => OnQuit?.Invoke()),
    ];

    // ── Menu plumbing ─────────────────────────────────────────────────────────
    // Each option asks the config what it should look like when the menu opens rather than
    // being ticked once at startup: the file is editable by hand and reloadable from this very
    // menu, so anything remembering its own state would start lying the moment it was.

    private static NativeMenuItem Check(string header, Func<bool> isOn, Action<bool> set)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isOn(),
            CommandParameter = isOn,
        };

        item.Click += (_, _) =>
        {
            set(!isOn());
            item.IsChecked = isOn();
        };

        return item;
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private static NativeMenuItem Submenu(string header, params NativeMenuItem[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items) menu.Add(item);

        void Retick()
        {
            foreach (var item in items)
                if (item.CommandParameter is Func<bool> isChosen)
                    item.IsChecked = isChosen();
        }

        // Twice, because neither moment is reliable on its own: picking an option has to move
        // the tick off the old one straight away, and opening the menu has to account for the
        // file having been edited by hand behind its back.
        foreach (var item in items) item.Click += (_, _) => Retick();
        menu.Opening += (_, _) => Retick();

        return new NativeMenuItem { Header = header, Menu = menu };
    }

    private static NativeMenuItem Choice(string header, Func<bool> isChosen, Action choose)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isChosen(),
            CommandParameter = isChosen,
        };

        item.Click += (_, _) => choose();
        return item;
    }

    private NativeMenuItem Room(string header, EdgeRoom room) =>
        Choice(header, () => _config.RoomOnTheRight == room, () => Change(() => _config.RoomOnTheRight = room));

    private NativeMenuItem Size(string header, double size) =>
        Choice(header, () => Math.Abs(_config.Size - size) < 0.001, () => Change(() => _config.Size = size));

    private NativeMenuItem Crew(string header, int builders) =>
        Choice(header, () => _config.Builders == builders, () => Change(() => _config.Builders = builders));

    private NativeMenuItem Opacity(string header, double opacity) =>
        Choice(header, () => Math.Abs(_config.Opacity - opacity) < 0.001, () => Change(() => _config.Opacity = opacity));

    private void Change(Action change)
    {
        change();
        _config.Save();
        OnConfigChanged?.Invoke();
    }

    /// <summary>
    /// Open <c>tower.json</c> in whatever the machine opens JSON with. The brick colours are too
    /// open-ended for a menu, so the menu's job there is just to make the file findable.
    /// </summary>
    private void EditConfig()
    {
        try
        {
            if (!File.Exists(TowerConfig.DefaultPath)) _config.Save();
            Process.Start(new ProcessStartInfo(TowerConfig.DefaultPath) { UseShellExecute = true });
        }
        catch
        {
            // No editor associated with .json, or the shell refused. A desk toy does not get to
            // interrupt anyone over it.
        }
    }
}
