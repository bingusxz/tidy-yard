using System.Text.Json.Serialization;

namespace TidyYard.Models;

/// <summary>How a fence arranges its items.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FenceLayout>))]
public enum FenceLayout
{
    /// <summary>Icons wrap into rows.</summary>
    Grid,

    /// <summary>One horizontal strip that scrolls sideways.</summary>
    Row,

    /// <summary>Small icons with names, one per line.</summary>
    List,
}

/// <summary>Where a fence's title sits in its title bar.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FenceTitleAlign>))]
public enum FenceTitleAlign
{
    Left,
    Center,
    Right,
}

/// <summary>A fence: a desktop panel that shows the contents of one folder.</summary>
public sealed class Fence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "Fence";

    /// <summary>
    /// The folder of the fence's first tab. Kept for fences saved before tabs existed, and so that
    /// Tidy Desktop can find the folder of a category fence. Removing the fence never touches it.
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// The fence's tabs. A fence with one tab shows no tab bar. Older fences have none saved;
    /// <see cref="EnsureTabs"/> gives them a single tab for <see cref="FolderPath"/>.
    /// </summary>
    public List<FenceTab> Tabs { get; set; } = [];

    /// <summary>The tab that was showing when the fence was last used.</summary>
    public Guid? ActiveTabId { get; set; }

    /// <summary>Makes sure there is at least one tab and that the first tab matches <see cref="FolderPath"/>.</summary>
    public void EnsureTabs()
    {
        if (Tabs.Count == 0)
        {
            Tabs.Add(new FenceTab { Title = "Main", FolderPath = FolderPath });
        }

        FolderPath = Tabs[0].FolderPath;
    }

    /// <summary>Physical-pixel bounds on screen.</summary>
    public WindowBounds? Bounds { get; set; }

    /// <summary>Rolled up = only the title bar is shown.</summary>
    public bool IsRolledUp { get; set; }

    /// <summary>A rolled-up fence drops open while the pointer is over it, and rolls back up when it leaves.</summary>
    public bool PeekOnHover { get; set; } = true;

    /// <summary>Locked fences cannot be moved or resized (each fence has its own lock).</summary>
    public bool IsLocked { get; set; }

    public FenceLayout Layout { get; set; } = FenceLayout.Grid;

    /// <summary>The fence's own glass colour as #RRGGBB, or null for the theme tint.</summary>
    public string? TintColor { get; set; }

    public FenceTitleAlign TitleAlign { get; set; } = FenceTitleAlign.Center;

    /// <summary>
    /// The fence grows or shrinks to fit its contents when it opens and when its contents change, so nothing is
    /// cut off. Turn off to keep a height set by hand.
    /// </summary>
    public bool FitHeight { get; set; } = true;

    // The header (title) and the files (item names) are styled separately. Null = the theme's own.

    /// <summary>Colour of item names as #RRGGBB.</summary>
    public string? TextColor { get; set; }

    /// <summary>Font of item names.</summary>
    public string? FontFamilyName { get; set; }

    /// <summary>Size (WPF units) of item names.</summary>
    public double? TextSize { get; set; }

    /// <summary>Colour of the title as #RRGGBB.</summary>
    public string? TitleTextColor { get; set; }

    /// <summary>Font of the title.</summary>
    public string? TitleFontFamilyName { get; set; }

    /// <summary>Size (WPF units) of the title.</summary>
    public double? TitleTextSize { get; set; }

    /// <summary>Height (WPF units) to return to when the fence is rolled back down.</summary>
    public double ExpandedHeight { get; set; } = 280;
}

/// <summary>One tab of a fence: a name and the folder it shows. Removing a tab never touches the folder.</summary>
public sealed class FenceTab
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "Main";

    public string FolderPath { get; set; } = string.Empty;
}
