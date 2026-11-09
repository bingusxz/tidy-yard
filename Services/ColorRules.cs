using System.Windows;
using System.Windows.Media;
namespace TidyYard.Services;

public static class ColorRules
{
    public static Color Over(Color foreground, Color background)
    {
        var a=foreground.A/255.0;
        return Color.FromRgb((byte)Math.Round(foreground.R*a+background.R*(1-a)),(byte)Math.Round(foreground.G*a+background.G*(1-a)),(byte)Math.Round(foreground.B*a+background.B*(1-a)));
    }

    public static Color Readable(Color requested, IEnumerable<Color> backgrounds, double minimum=4.5)
    {
        var surfaces=backgrounds.ToArray();
        if(surfaces.Length==0)return requested;
        double Score(Color c)=>surfaces.Min(bg=>ThemeService.ContrastRatio(c,bg));
        var end=Score(Colors.Black)>=Score(Colors.White)?Colors.Black:Colors.White;
        for(var i=0;i<=100;i++)
        {
            var t=i/100.0;
            var c=Color.FromRgb((byte)Math.Round(requested.R*(1-t)+end.R*t),(byte)Math.Round(requested.G*(1-t)+end.G*t),(byte)Math.Round(requested.B*(1-t)+end.B*t));
            if(Score(c)>=minimum)return c;
        }
        return end;
    }

    public static IReadOnlyList<Color> ThemeSurfaces(ResourceDictionary resources)
    {
        Color Get(string key)=>((SolidColorBrush)resources["Brush."+key]).Color;
        var window=Get("Window");var surfaces=new List<Color>();
        foreach(var key in new[]{"Window","Card","Input","Raised","Surface"})
        {
            var surface=Over(Get(key),window);surfaces.Add(surface);
            foreach(var state in new[]{"Hover","Pressed","AccentSoft"})surfaces.Add(Over(Get(state),surface));
        }
        return surfaces;
    }
}
