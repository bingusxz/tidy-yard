using System.Windows;
using System.Windows.Media;
namespace TidyYard.Services;

public static class PlotAppearance
{
    public static void Apply(ResourceDictionary resources, double opacity, string? tintHex)
    {
        foreach(var key in new[]{"Text","TextMuted","TextDim","Window","AccentText","Danger","Warning"})resources.Remove("Brush."+key);
        var custom=ThemeService.TryParseHex(tintHex,out var tint);
        if(!custom && opacity>=0.35)return;
        var surface=opacity<0.35?Color.FromRgb(0x1A,0x20,0x29):tint;
        resources["Brush.Window"]=Frozen(surface);
        foreach(var key in new[]{"Text","TextMuted","TextDim","AccentText","Danger","Warning"})
        {
            var original=((SolidColorBrush)Application.Current.FindResource("Brush."+key)).Color;
            var readable=ColorRules.Readable(original,new[]{surface},key=="Text"?7:4.55);
            resources["Brush."+key]=Frozen(readable);
        }
    }
    private static SolidColorBrush Frozen(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
}
