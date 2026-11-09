using System.Windows;
using System.Windows.Media;
using TidyYard.Models;

namespace TidyYard.Services;

/// <summary>A ready-made look for the notepad: font, ink, paper and whether the sheet is ruled.</summary>
public sealed record NotepadDesign(string Key, string Name, string Font, int Size, string Ink, string Paper, bool Ruled)
{
    public override string ToString() => Name;
}

public static class NotepadDesigns
{
    public const string Custom = "Custom";

    public static readonly IReadOnlyList<NotepadDesign> Presets =
    [
        new("Classic", "Classic Notepad", "Consolas", 14, "#000000", "#FFFFFF", false),
        new("Paper", "Paper", "Segoe Print", 15, "#2D2B27", "#FAF9F6", true),
        new("Legal", "Legal pad", "Segoe Print", 15, "#1F3A5F", "#F5E8B8", true),
        new("Typewriter", "Typewriter", "Courier New", 14, "#2B2B2B", "#F1EADC", false),
        new("Night", "Night", "Segoe UI", 14, "#ECE7DB", "#2B2A27", true),
    ];

    public static readonly NotepadDesign CustomDesign = new(Custom, "Custom", "", 0, "", "", false);

    /// <summary>The presets plus Custom, for the design picker.</summary>
    public static IReadOnlyList<NotepadDesign> All { get; } = [.. Presets, CustomDesign];

    /// <summary>The preset whose values equal these settings, or Custom.</summary>
    public static NotepadDesign Match(NotepadSettings s) =>
        Presets.FirstOrDefault(p =>
            string.Equals(p.Font, s.FontFamily, StringComparison.OrdinalIgnoreCase) && p.Size == s.FontSize
            && string.Equals(p.Ink, s.InkColor, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Paper, s.PaperColor, StringComparison.OrdinalIgnoreCase) && p.Ruled == s.RuledLines)
        ?? CustomDesign;

    public static void Apply(NotepadDesign design, NotepadSettings s)
    {
        if (design.Key == Custom) return;
        s.Design = design.Key;
        s.FontFamily = design.Font;
        s.FontSize = design.Size;
        s.InkColor = design.Ink;
        s.PaperColor = design.Paper;
        s.RuledLines = design.Ruled;
    }
}

/// <summary>Colours and metrics a sheet is drawn with, worked out from the settings and a paper colour.</summary>
public sealed record NotepadLook(
    Color Paper, Color Ink, Color Muted, Color Rule, Color Hover, Color Pressed, Color Band,
    FontFamily Font, double FontSize, double LinePitch, bool Ruled)
{
    public static NotepadLook From(NotepadSettings s, Color? paperOverride = null)
    {
        var paper = paperOverride ?? (ThemeService.TryParseHex(s.PaperColor, out var p) ? p : Color.FromRgb(0xFA, 0xF9, 0xF6));
        var requestedInk = ThemeService.TryParseHex(s.InkColor, out var i) ? i : Color.FromRgb(0x2D, 0x2B, 0x27);

        // A chosen ink is kept unless it cannot be read on this paper (for example light ink on a pale note colour).
        var ink = ColorRules.Readable(requestedInk, new[] { paper }, 7);
        var muted = ColorRules.Readable(ColorRules.Over(Color.FromArgb(0xC0, ink.R, ink.G, ink.B), paper), new[] { paper }, 4.55);
        var paperIsLight = ThemeService.ContrastRatio(paper, Colors.White) < ThemeService.ContrastRatio(paper, Colors.Black);
        var overlay = paperIsLight ? Colors.Black : Colors.White;
        var size = Math.Clamp(s.FontSize, 9, 40);
        var family = string.IsNullOrWhiteSpace(s.FontFamily) ? "Segoe UI" : s.FontFamily;
        return new NotepadLook(
            paper, ink, muted,
            ColorRules.Over(Color.FromArgb(0xB0, ink.R, ink.G, ink.B), paper),
            Color.FromArgb(paperIsLight ? (byte)0x14 : (byte)0x1F, overlay.R, overlay.G, overlay.B),
            Color.FromArgb(paperIsLight ? (byte)0x26 : (byte)0x33, overlay.R, overlay.G, overlay.B),
            Color.FromArgb(paperIsLight ? (byte)0x14 : (byte)0x1A, overlay.R, overlay.G, overlay.B),
            new FontFamily($"{family}, Segoe UI"), size, Math.Round(size * 1.75), s.RuledLines);
    }
}
