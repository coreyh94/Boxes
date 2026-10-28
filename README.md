# Desktop Boxes

A native Windows 11 desktop organiser using .NET 10 Windows Forms. No browser engine, network service, or third-party packages.

## Build and run

Install the .NET 10 Desktop Runtime for published builds, or the SDK to build:

```powershell
dotnet publish DesktopBoxes -c Release -o dist
.\dist\DesktopBoxes.exe
```

## Controls

- Create a box from **New box** in a box's `⋯` menu or the tray menu, or double-click the tray icon. New boxes go in free grid-aligned space. Boxes are clean cards: an editable title, a `⋯` options button, and your items. Drag the header to move; drag the bottom-right corner to resize. Text and spacing follow Windows display scaling.
- Click **⋯**, or right-click a title/background, for the colour palette and deletion.
- Drag the left edge of a title bar to move; a 48-pixel grid appears and placement snaps on release.
- Edit the title directly. Drag the bottom edge to resize.
- Drop files, folders, or shortcuts into a box. Boxes saves an independent copy; a short note appears at the bottom of the box while saving or if something fails. Slots fill left to right, then top to bottom. Like desktop icons: click to select, double-click (or Enter) to open, press and move to drag. Click empty space or outside the box to clear the selection. Right-click an item to open it, remove it, or show its saved file. Drag an item to another box to move it, or within a box to reorder; an accent bar shows where it will land, and boxes scroll when you drag near their top or bottom edge. These moves stay inside Boxes, so saved files are never moved out of the library.
- Original files remain in place. Once an item appears in the box, deleting the desktop original does not remove the saved copy. Files and folders are separate copies, not synced; shortcuts still launch their original installed app or target.
- Removing an item or box never deletes its saved files. **Open saved items** in box options (or **Saved items** in the tray) opens the library.
- Rounded corners, tinted dark surfaces, colour accents, keyboard focus/hover highlights, compact controls, and a clear empty state keep the interface quiet. Alt+F4 hides a box; use its options to remove it.
- Boxes runs quietly in the system tray (the Boxes icon beside the clock). Tray controls show/hide boxes, open saved items, open **Settings…**, and exit.
- **Settings…** (tray, or any box's options menu) has **Start Boxes with Windows** (on by default) and **Show icon next to the clock**. Startup uses the per-user Run key, so it also appears in Windows Settings › Apps › Startup; switching it off there is respected. Each launch keeps the registered path pointed at the running executable. The tray icon is pinned beside the clock once per executable path; after that, Windows' own taskbar setting wins. Preferences live in `%LOCALAPPDATA%\DesktopBoxes\settings.json`.
- Boxes is a per-user tray app rather than a Windows service: services run in an isolated session and cannot show windows on your desktop.
- Layout is saved under `%LOCALAPPDATA%\DesktopBoxes\layout.json`.

## Independent saved items

Copies live in `%LOCALAPPDATA%\DesktopBoxes\Library`. Imports run in the background, complete in a staging directory, and are published before the saved layout references them. Failed imports do not publish a partial item. Copies of the same source are reused; differently located files with the same name remain distinct. Removing a box retains its library files. Large folders require additional disk space because their contents are copied.

Existing layouts are backed up before migration. Available originals are copied and their layout entries updated automatically; missing originals remain listed with an inline instruction to drop them again. Items deleted before a copy was made cannot be recovered by the app. Junctions, symbolic links, and cloud placeholders are rejected instead of being recursively followed; use regular local files/folders. Shortcuts preserve their launch arguments, working folder, and target, but do not back up the installed application itself.

## Icon and box avoidance

Uses Explorer's supported IFolderView2 / SelectAndPositionItems API. Icons move into free slots immediately when creating, moving, or resizing a box; periodic checks handle desktop changes. When icons need moving, Explorer auto-arrange and snap-to-grid are disabled so Explorer cannot undo the reserved spaces. These settings and icon positions are not restored on exit.

Box moves/resizes that overlap another box or leave the monitor work area are rejected. If there is insufficient room for icons, new placements are rejected. If an existing layout becomes unsafe or Explorer is unavailable, boxes hide. Free space and use **Show boxes** in the tray to retry. The tray tooltip and `%LOCALAPPDATA%\DesktopBoxes\status.log` show the reason.

## Monitors and resolution changes

Each box remembers a saved spot, which only changes when you move or resize it. Layout changes are saved immediately. When monitors are added or removed, or a resolution or scale changes, Boxes waits for Windows to settle (about 1.5 s) and re-fits. Boxes whose saved spot is still on screen stay there. The rest borrow free grid space on the nearest monitor, shrunk if necessary, without overwriting their saved spot. When the monitor or resolution comes back, they return home. If there is no room at all, the box is hidden until there is. Moving a relocated box makes its new position its saved spot.

Microsoft explains why old ListView messages are unsuitable here: https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948 .

## Validation and remaining limitations

Verified: clean Release build/publish; UI smoke test creating a second box; placement tests for collisions, stable repeated placement, full screens, and negative monitor origins; live Explorer position reads and accepted icon moves; live checks that icon cells do not intersect saved boxes and saved boxes do not intersect each other.

Developer checks: `--smoke-test` (imports an item through the app, deletes its original, verifies retention, exercises New box, and renders normal/compact controls), `--library-tests` (file/folder/URL/Windows shortcut deletion, shortcut metadata, reload, same-name collisions, self-import and failed-copy cleanup), `--placement-tests`, `--desktop-check` (writes a snapshot beside the executable), `--scroll-check` (loads the saved boxes off-screen and prints their scroll metrics), and `--verify-layout` (checks current Explorer positions against saved boxes).

Normal and compact UI layouts were rendered from the actual controls and visually inspected. Windows tool windows were not exposed by the UI inspection inventory for live screenshots. Mixed-DPI changes, Explorer restarts, and Show Desktop need broader hands-on testing. Boxes use ordinary borderless windows maintained near the desktop in the Z order, rather than an Explorer extension. Shell virtual items such as Recycle Bin cannot be dropped as file shortcuts.
