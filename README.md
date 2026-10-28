# Desktop Boxes

A small native Windows 11 desktop organiser built with .NET 10 Windows Forms. No browser engine, network service, or third-party packages.

## Run

Install the .NET 10 Desktop Runtime, then run DesktopBoxes.exe from a published build. Build locally with:

```powershell
dotnet publish DesktopBoxes -c Release -o dist
.\dist\DesktopBoxes.exe
```

- Right-click a box for the six-colour palette, a new box, or deletion.
- Drag the left edge of its title bar to move it; a 48-pixel grid appears and the box snaps on release.
- Edit the title directly. Drag the bottom edge to resize.
- Drop files, folders, or shortcuts into a box. They fill slots left to right, then top to bottom. Click to open; right-click to remove.
- Original files remain in place. Removing a box never deletes its files.
- The tray menu shows/hides boxes, creates boxes, enables **Start with Windows**, or exits. Enable startup after placing the executable in its permanent folder.
- Layout is saved under `%LOCALAPPDATA%\DesktopBoxes\layout.json`.

## Prototype limitations

Explorer integration is experimental and requires hands-on Windows 11 validation. Covered desktop icons are moved to available slots every two seconds. Explorer's **Auto arrange icons must be off**; Windows prevents these moves otherwise. If there is no free space, icons are left in place. Positions are not restored on exit. Shell virtual items such as Recycle Bin cannot be dropped as file shortcuts.

Boxes are ordinary borderless windows maintained near the desktop in the Z order, rather than an Explorer extension. Show Desktop, Explorer restarts, foreground transitions, and mixed-DPI monitors require further validation. Boxes can overlap each other. Items cannot yet be manually reordered or transferred between boxes. Startup is opt-in via the tray menu; no startup setting is changed during build.

## Validation

Release compilation, publishing, and a UI construction/launch/close smoke test verified on Windows with .NET SDK 10.0.400. No live desktop acceptance testing has yet been completed. Before relying on it, check drop/open/remove, rename/resize/colour persistence after exit, grid alignment on each monitor, icon relocation with and without auto-arrange, tray startup toggle, and Show Desktop/application switching.

Icon positioning uses Windows list-view messages: https://learn.microsoft.com/en-us/windows/win32/controls/lvm-setitemposition . Auto-arrange prevents manual placement.

