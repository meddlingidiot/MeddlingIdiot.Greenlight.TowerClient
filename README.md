# MeddlingIdiot.Greenlight.TowerClient

A crew of tiny builders putting up a tower on your taskbar. They build while the build is
green. It sways while it is yellow. When it goes red it falls down, and everybody runs around
screaming.

A runnable reference consumer of the [Greenlight](https://github.com/meddlingidiot/MeddlingIdiot.Greenlight)
SDK, and a demonstration of how little an app needs to do to use it:

- **Green** — they fetch bricks from the pile, carry them to the ladder, climb up and lay them,
  a course at a time. The higher it gets, the longer the climb, so a tall tower is a slow tower.
  Topped out, the ladder comes away and a flag goes up. While a CI build is running they work
  faster, because somebody is watching.
- **Yellow** — tools down. Anybody on the ladder climbs off it, everybody stands and looks up,
  and the tower sways. The taller it is, the further it goes.
- **Red** — down it comes. The tower topples away from the edge of the screen, the bricks
  tumble, bounce and settle into a heap, and anybody who was on the ladder at the time is not
  any more. Then everybody runs about with their arms in the air screaming *AAAH!*, *HELP!* and
  *WHO BROKE IT?!* for as long as the pipeline stays broken.
- **Green again** — somebody says *Right. Again.*, they carry the rubble back to the pile a
  piece at a time, and start over from the ground.
- **No Greenlight at all** — the site dims and nobody moves. A crew building on a
  four-minute-old snapshot would be lying to you.

The point of it is what it does **not** have. No Azure DevOps client, no GitHub client, no
token, no polling loop — everything it knows arrives through the SDK, from the Greenlight
already running on the machine. Strip out the drawing and the tray icon and the integration is
about twenty lines, all of them in [`App.cs`](Greenlight.TowerClient/App.cs).

## Where it stands

On the taskbar, at the right-hand end — but not in the corner. A maximized editor keeps its
vertical scrollbar down the very right-hand edge of the screen, and a tower in the corner would
be standing on the one control that is always being reached for. So the site stands a
scrollbar's width in, just inside it, over the bottom of the code where nothing is being clicked
on. It is click-through anyway; this is about not having a tower in front of the thumb.

IDEs like Rider and Visual Studio also keep a strip of tool-window buttons outside the editor's
scrollbar. **Room on the right → A scrollbar and a tool strip** in the tray moves it in past
that too, and `ExtraInset` in the file takes it further still for anything wider.

It works out the scrollbar's width at the screen's own scale, so the gap is the same gap at 100%
and at 175%.

## Running it

```bash
dotnet run --project Greenlight.TowerClient
```

To see the whole story without breaking anything first:

```bash
dotnet run --project Greenlight.TowerClient -- --demo
```

which ignores Greenlight and loops through green, yellow and red on its own, with the crew
hurrying so that a lap takes a couple of minutes rather than a morning.

Windows only: the click-through overlay and the work-area maths are Win32. The SDK itself is
not — it is plain .NET, and the same twenty lines work anywhere. One copy runs per session; a
second launch leaves quietly rather than putting two crews on the same patch of taskbar.

## The tray

Everything lives on the mascot in the notification area — the site itself cannot be clicked,
because it spends all day standing in front of your editor:

- **Tower on the taskbar** — pack the site away and put it back up. Clicking the icon does the
  same. It comes back exactly as it was left, half built or in pieces; the off switch is not a
  way of getting out of a rebuild.
- **Which end** — right or left.
- **Room on the right** — none, a scrollbar, or a scrollbar and a tool strip.
- **How big**, **How many builders**, **How solid**.
- **Let them scream** — off, they still run around; they just do it without the speech bubbles.
- **Start with Windows** — read from the registry every time the menu opens, so it agrees with
  Task Manager's Startup tab rather than with what we last wrote there.
- **Edit the colours…** — opens `tower.json`. **Reload the file** picks up hand edits without a
  restart.

## The file

`%AppData%\Greenlight.Tower\tower.json`, written with the defaults on first run:

```json
{
  "Side": "Right",
  "RoomOnTheRight": "Scrollbar",
  "ExtraInset": 0,
  "Size": 1.0,
  "Builders": 5,
  "Opacity": 1.0,
  "Screaming": true,
  "BrickColors": [ "#C9A66B", "#B8925A", "#D8BC84", "#A9824E" ]
}
```

Sandstone rather than red brick, so that a tower standing on a passing build is not also, from
across the room, a red thing on the taskbar.

A file that cannot be parsed falls back to the defaults rather than refusing to start, and a
crew of 500 or a size of -3 is clamped on the way in.

## How it is put together

| | |
|---|---|
| [`App.cs`](Greenlight.TowerClient/App.cs) | The whole Greenlight integration, and the `--demo` loop |
| [`TowerSimulation.cs`](Greenlight.TowerClient/TowerSimulation.cs) | The tower, the crew, the rubble, and what each colour does to them. No Avalonia, so it is testable |
| [`SitePlacement.cs`](Greenlight.TowerClient/SitePlacement.cs) | Where the site stands, and how much room it leaves for a scrollbar |
| [`TowerCanvas.cs`](Greenlight.TowerClient/TowerCanvas.cs) | The drawing: bricks, ladder, flag, rubble, builders and speech bubbles |
| [`TowerWindow.cs`](Greenlight.TowerClient/TowerWindow.cs) | The click-through window, and the frame loop |
| [`TowerTray.cs`](Greenlight.TowerClient/TowerTray.cs) | The tray icon and its menu |

The collapse is ordinary falling-brick physics with two pieces of cheating, both of which the
tests exist to hold on to.

The first is that a brick coming off the tower ignores the rest of the rubble for its first
third of a second. Without that the tower does not fall — it re-stacks: the bottom course lands
in the first frame, the one above it is already touching it and settles on top, and so on up,
until there is a tower standing exactly where it was that is officially rubble.

The second is that rubble slumps. Each brick lands on whatever is under its middle, and left at
that the heap grows sheer columns taller than the tower that made them. A brick that would come
to rest more than a course above its neighbours slides down the side instead, and the heap comes
out the shape of a heap.

The clean-up picks rubble off the top of the heap first, and anything resting on a piece that has
been carried away drops onto what is under it now — the one thing the clean-up must never leave
behind is a brick hanging in the air where its support used to be.

```bash
dotnet test
```

## Licence

The code is MIT — see [LICENSE](LICENSE). That includes the builders, the tower and the rubble,
which are drawn by the code.

**The logo is not.** The MeddlingIdiot mascot icon in
[`Greenlight.TowerClient/Assets`](Greenlight.TowerClient/Assets) is all rights reserved: it is
not under the MIT License, and it is not sharable or reusable in forks or anything else. Fork
the code, bring your own icon. See [TRADEMARKS.md](TRADEMARKS.md).
