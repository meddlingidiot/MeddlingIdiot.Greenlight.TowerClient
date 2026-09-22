# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added

- First cut: a crew of tiny builders putting up a tower on the taskbar, lit from the Greenlight
  running on the machine. They build on green, stop and watch it sway on yellow, and run around
  screaming when it falls down on red. With no Greenlight to ask the site dims and nobody moves.
- Building that slows as it goes: every brick is fetched from the pile, carried to the ladder,
  climbed up and laid, so the higher the tower the longer each course takes. A flag goes up when
  it tops out, and the ladder comes away.
- A faster crew while a CI build is running.
- Sway that grows with the height of the tower and eases in and out, so a tower that stops
  swaying settles upright rather than snapping.
- A collapse that topples away from the edge of the screen: every brick thrown across the site,
  harder the higher up it was, tumbling, bouncing and coming to rest roughly flat in a heap
  that slumps rather than stacking into columns. Anybody on the ladder falls off, and anything
  anybody was carrying joins the rubble.
- Screaming - *AAAH!*, *HELP!*, *WHO BROKE IT?!* - from a crew running about with their arms in
  the air for as long as the pipeline stays broken. Never more than half of them at once, never
  two of the same scream at once, never one bubble on top of another. Switchable, for the
  running without the bubbles.
- A clean-up before the rebuild: the rubble is carried back to the pile a piece at a time, top of
  the heap first, before anybody lays a course.
- A site that stands on the taskbar at the right-hand end, just inside the scrollbar of whatever
  is maximized behind it, worked out at the screen's own scale. Room for an IDE's tool-window
  strip as well from the tray, and more still from the file.
- A tray menu for which end, how much room, how big, how many builders, how solid and whether
  they scream - each written straight back to `tower.json`.
- A site that survives being packed away from the tray: it comes back half built, or in pieces,
  exactly as it was left.
- `--demo`, which loops through green, yellow and red on its own without a Greenlight.
- One copy per session. A second launch leaves quietly, rather than putting two crews on the
  same patch of taskbar.
- "Start with Windows" in the tray menu, registering the stable shim beside the install rather
  than the versioned copy an update would move.
