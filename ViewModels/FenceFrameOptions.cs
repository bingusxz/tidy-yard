using TidyYard.Models;

namespace TidyYard.ViewModels;

/// <summary>One choice in the corners picker.</summary>
public sealed record CornerOption(string Name, FenceCorners Value)
{
    public override string ToString() => Name;
}

/// <summary>One choice in the border picker: a name and the line width it draws (0 = no line).</summary>
public sealed record BorderOption(string Name, double Width)
{
    public override string ToString() => Name;
}
