using System.Runtime.InteropServices;
using System.Text.Json;

namespace DesktopBoxes;

/// <summary>Drives a note box's pad with key messages posted straight to it, so nothing needs focus and no other app can receive them.</summary>
internal static class NoteTests
{
    [DllImport("user32.dll")] static extern bool PostMessage(nint window, uint message, nint wide, nint low);
    [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);

    static void Press(NotePad pad, Keys keys)
    {
        var before = new byte[256]; GetKeyboardState(before);
        var held = (byte[])before.Clone();
        if (keys.HasFlag(Keys.Control)) held[(int)Keys.ControlKey] = 0x80;
        if (keys.HasFlag(Keys.Shift)) held[(int)Keys.ShiftKey] = 0x80;
        SetKeyboardState(held);
        PostMessage(pad.Handle, 0x100, (nint)(keys & Keys.KeyCode), 1); Application.DoEvents();
        PostMessage(pad.Handle, 0x101, (nint)(keys & Keys.KeyCode), unchecked((nint)0xC0000001)); Application.DoEvents();
        SetKeyboardState(before);
    }
    static void Check(bool passed, string what) { if (!passed) throw new InvalidOperationException("Note test failed: " + what); }
    static Box Open(Boxes owner, State state) { state.X = state.Y = -20000; var box = new Box(owner, state); box.Show(); Application.DoEvents(); return box; }

    internal static void Run()
    {
        var owner = Boxes.Detached();
        using var box = Open(owner, new State { Name = "Test", Note = "Milk\r\nBread" });
        var pad = box.NotePad;
        Check(box.NoteText == "Milk\r\nBread" && box.State.NoteRtf == null, "plain note from an older version didn't load");

        pad.Select(0, 4);
        Press(pad, Keys.Control | Keys.B); Check(pad.SelectionFont!.Bold, "Ctrl+B");
        Press(pad, Keys.Control | Keys.U); Check(pad.SelectionFont!.Bold && pad.SelectionFont.Underline, "Ctrl+U");
        Press(pad, Keys.Control | Keys.T); Check(pad.SelectionFont!.Bold && pad.SelectionFont.Underline && pad.SelectionFont.Strikeout, "Ctrl+T");
        Press(pad, Keys.Control | Keys.T); Check(!pad.SelectionFont!.Strikeout && pad.SelectionFont.Bold, "Ctrl+T off");
        Check(box.NoteText == "Milk\r\nBread", "a shortcut changed the text");
        Check(box.State.NoteRtf?.Contains(@"\b") == true, "formatting wasn't kept in the box's state");
        Check(box.State.Note == "Milk\r\nBread", "plain text line endings changed");

        // A selection that's only partly bold goes all bold first, then all plain, leaving other effects alone.
        pad.SelectAll();
        Press(pad, Keys.Control | Keys.B); Check(Effect(pad, 0, 11, f => f.Bold), "mixed selection to bold");
        pad.SelectAll(); Press(pad, Keys.Control | Keys.B); Check(Effect(pad, 0, 11, f => !f.Bold), "bold off");
        pad.Select(0, 4); Check(pad.SelectionFont!.Underline, "underline lost when bold changed");

        // With nothing selected, an effect applies to what's typed next.
        pad.Select(pad.TextLength, 0);
        Press(pad, Keys.Control | Keys.T); Press(pad, Keys.X);
        pad.Select(pad.TextLength - 1, 1); Check(pad.SelectedText == "x" && pad.SelectionFont!.Strikeout, "typing after Ctrl+T");

        pad.SelectAll();
        Press(pad, Keys.Control | Keys.Shift | Keys.L); pad.Select(0, 0); Check(pad.SelectionBullet, "dot points on");
        pad.Select(pad.TextLength, 0); Check(pad.SelectionBullet, "dot points on every selected line");

        // Formatting survives a save and reload, and follows the box's style rather than the style it was saved in.
        var saved = JsonSerializer.Deserialize<State>(JsonSerializer.Serialize(box.State))!;
        saved.Look = BoxStyle.Dark; saved.Colour = SwatchRow.Palette[0].Colour.ToArgb();
        using (var reloaded = Open(owner, saved))
        {
            var again = reloaded.NotePad; again.Select(0, 4);
            Check(again.SelectionFont!.Underline && again.SelectionBullet, "formatting lost on reload");
            Check(again.SelectionColor.ToArgb() == Theme.InkOn(reloaded.BackColor).ToArgb(), "reloaded text kept its old colour");
        }

        using var broken = Open(owner, new State { Note = "Kept", NoteRtf = "not rtf" });
        Check(broken.NoteText == "Kept", "unreadable formatting lost the text");

        // Enter carries a dot point on to the next line, and ends the list from an empty one.
        using var list = Open(owner, new State { Note = "" });
        var items = list.NotePad;
        Press(items, Keys.Control | Keys.Shift | Keys.L); Press(items, Keys.A); Press(items, Keys.Enter);
        Check(items.Text == "a\n" && items.SelectionBullet, "Enter after a dot point");
        Press(items, Keys.Enter);
        Check(items.Text == "a\n" && !items.SelectionBullet, "Enter on an empty dot point");

        // Ctrl+I would otherwise replace the selected text with a tab.
        items.SelectAll(); Press(items, Keys.Control | Keys.I);
        Check(items.Text == "a\n", "Ctrl+I changed the text");
        items.Select(0, 1); Check(items.SelectionFont!.Italic, "Ctrl+I");

        // Undoing only formatting raises no text change, but must still reach what gets saved.
        var italic = list.State.NoteRtf;
        Press(items, Keys.Control | Keys.Z); Check(list.State.NoteRtf != italic, "undone formatting wasn't kept");
    }
    /// <summary>Whether every character in a range passes, for selections whose font is mixed.</summary>
    static bool Effect(NotePad pad, int start, int length, Func<Font, bool> test)
    {
        for (int i = start; i < start + length && i < pad.TextLength; i++) { pad.Select(i, 1); if (!test(pad.SelectionFont!)) return false; }
        return true;
    }
}
