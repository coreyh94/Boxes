using System.Runtime.InteropServices;

namespace DesktopBoxes;

/// <summary>The text pad of a note box: bold, italic, underline, strikethrough and dot points. Pasting brings in plain text only, so a note keeps its own font and colours.</summary>
internal sealed class NotePad : RichTextBox
{
    /// <summary>Character effects; each value is both the "set consistently" mask bit and the "switched on" effect bit.</summary>
    internal const uint Bold = 1, Italic = 2, Underline = 4, Strikethrough = 8;
    const uint GetCharFormat = 0x43A, SetCharFormat = 0x444;
    const nint Selection = 1;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CharFormat
    {
        public int Size; public uint Mask, Effects; public int Height, Offset, Colour; public byte CharacterSet, Pitch;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Face;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint SendMessage(nint window, uint message, nint wide, ref CharFormat format);

    internal NotePad()
    {
        BorderStyle = BorderStyle.None; Dock = DockStyle.Fill; ScrollBars = RichTextBoxScrollBars.Vertical; DetectUrls = false; BulletIndent = Theme.S(14);
        Font = new Font("Segoe UI", 14 * Theme.Scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    /// <summary>The text without formatting. RichTextBox reports bare \n line endings; a saved note uses \r\n.</summary>
    internal string PlainText => Text.ReplaceLineEndings("\r\n");

    /// <summary>Shows a saved note. Notes from before 1.8.0 have no formatting, and unreadable formatting falls back to the plain text.</summary>
    internal void Load(string? text, string? rtf)
    {
        if (rtf != null) try { Rtf = rtf; return; } catch (ArgumentException) { }
        Text = text ?? "";
    }

    /// <summary>Switches an effect on across the selection, or off if all of it already has the effect. With nothing selected, it applies to what's typed next.</summary>
    internal void Toggle(uint effect)
    {
        var format = new CharFormat { Size = Marshal.SizeOf<CharFormat>() };
        SendMessage(Handle, GetCharFormat, Selection, ref format);
        bool on = (format.Mask & effect) != 0 && (format.Effects & effect) != 0;
        format = new CharFormat { Size = format.Size, Mask = effect, Effects = on ? 0 : effect };
        SendMessage(Handle, SetCharFormat, Selection, ref format);
    }
    internal void ToggleBullets() => SelectionBullet = !SelectionBullet;
    internal void PastePlain() => Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
    internal bool CanPastePlain => CanPaste(DataFormats.GetFormat(DataFormats.UnicodeText));
    bool OnEmptyBullet()
    {
        if (!SelectionBullet || SelectionLength > 0) return false;
        var text = Text; int at = SelectionStart;
        return (at == 0 || text[at - 1] == '\n') && (at >= text.Length || text[at] == '\n');
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        Action? action = e.KeyData switch
        {
            Keys.Control | Keys.B => () => Toggle(Bold),
            // Left alone, Ctrl+I replaces the selected text with a tab.
            Keys.Control | Keys.I => () => Toggle(Italic),
            Keys.Control | Keys.U => () => Toggle(Underline),
            Keys.Control | Keys.T => () => Toggle(Strikethrough),
            Keys.Control | Keys.Shift | Keys.L => ToggleBullets,
            Keys.Control | Keys.V or Keys.Shift | Keys.Insert => PastePlain,
            // Enter on an empty dot point ends the list, rather than leaving that empty point behind.
            Keys.Enter when OnEmptyBullet() => ToggleBullets,
            _ => null,
        };
        if (action != null) { action(); e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
}
