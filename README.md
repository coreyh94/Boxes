# Desktop Boxes

A native Windows 11 desktop organiser using .NET 10 Windows Forms. No browser engine, network service, or third-party packages.

## Build and run

Install the .NET 10 Desktop Runtime for published builds, or the SDK to build:

```powershell
dotnet publish DesktopBoxes -c Release -o dist
.\dist\DesktopBoxes.exe
```

## Controls

- Click **+ New box** in a box header to create a box in free grid-aligned space. The tray menu and double-clicking the tray icon also create boxes.
- Click **⋯**, or right-click a title/background, for the colour palette and deletion.
- Drag the left edge of a title bar to move; a 48-pixel grid appears and placement snaps on release.
- Edit the title directly. Drag the bottom edge to resize.
- Drop files, folders, or shortcuts into a box. Slots fill left to right, then top to bottom. Click to open; right-click an item to remove it.
- Original files remain in place. Removing a box never deletes files.
- Tray controls show/hide boxes, enable **Start with Windows**, and exit. Enable startup after putting the executable in its permanent folder.
- Layout is saved under `%LOCALAPPDATA%\DesktopBoxes\layout.json`.

## Icon and box avoidance

Uses Explorer's supported IFolderView2 / SelectAndPositionItems API. Icons move into free slots immediately when creating, moving, or resizing a box; periodic checks handle desktop changes. When icons need moving, Explorer auto-arrange and snap-to-grid are disabled so Explorer cannot undo the reserved spaces. These settings and icon positions are not restored on exit.

Box moves/resizes that overlap another box or leave the monitor work area are rejected. If there is insufficient room for icons, new placements are rejected. If an existing layout becomes unsafe or Explorer is unavailable, boxes hide. Free space and use **Show boxes** in the tray to retry. The tray tooltip and `%LOCALAPPDATA%\DesktopBoxes\status.log` show the reason.

Microsoft explains why old ListView messages are unsuitable here: https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948 .

## Validation and remaining limitations

Verified: clean Release build/publish; UI smoke test invoking the actual New box button; placement tests for collisions, stable repeated placement, full screens, and negative monitor origins; live Explorer position reads and accepted icon moves; live checks that icon cells do not intersect saved boxes and saved boxes do not intersect each other.

Developer checks: `--smoke-test`, `--placement-tests`, `--desktop-check` (writes a snapshot beside the executable), and `--verify-layout` (checks current Explorer positions against saved boxes).

Windows tool windows were not exposed by the UI inspection inventory, so screenshot-based visual inspection was unavailable. Mixed-DPI changes, Explorer restarts, and Show Desktop need broader hands-on testing. Boxes use ordinary borderless windows maintained near the desktop in the Z order, rather than an Explorer extension. Shell virtual items such as Recycle Bin cannot be dropped as file shortcuts. Items cannot yet be manually reordered or transferred between boxes. Startup remains opt-in through the tray menu.
