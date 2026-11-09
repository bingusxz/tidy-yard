using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TidyYard.Views;

/// <summary>Deforms an actual textured sheet: curl for peeling, irregular folds for crumpling.</summary>
public sealed class PaperAnimation : Viewport3D
{
    private const int Columns = 32;
    private const int Rows = 40;
    private readonly MeshGeometry3D _mesh;
    private readonly double _width;
    private readonly double _height;
    public PaperAnimation(ImageSource image, Color paper, double width, double height)
    {
        _width = Math.Max(1,width); _height = Math.Max(1,height);
        ClipToBounds = false; IsHitTestVisible = false;
        Camera = new OrthographicCamera(new Point3D(_width/2,_height/2,1000),new Vector3D(0,0,-1),new Vector3D(0,1,0),_width);
        _mesh = new MeshGeometry3D();
        for(var row=0;row<=Rows;row++) for(var col=0;col<=Columns;col++)
        {
            _mesh.Positions.Add(new Point3D(col*_width/Columns,_height-row*_height/Rows,0));
            _mesh.TextureCoordinates.Add(new Point((double)col/Columns,(double)row/Rows));
        }
        for(var row=0;row<Rows;row++) for(var col=0;col<Columns;col++)
        {
            var a=row*(Columns+1)+col;var b=a+1;var c=a+Columns+1;var d=c+1;
            // Counterclockwise front faces, with the texture's top at the sheet's top.
            foreach(var i in new[]{a,c,b,b,c,d}) _mesh.TriangleIndices.Add(i);
        }
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(180,180,180)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(95,95,95),new Vector3D(-0.4,-0.6,-1)));
        lights.Children.Add(new GeometryModel3D(_mesh,new DiffuseMaterial(new ImageBrush(image)))
        { BackMaterial = new DiffuseMaterial(new SolidColorBrush(paper)) });
        Children.Add(new ModelVisual3D { Content=lights });
    }

    public void Curl(double progress, bool fromLeft = false)
    {
        progress=Math.Clamp(progress,0,1);
        var radius=Math.Min(_width,_height)*0.16;
        var depth=progress*(_width*0.6+_height*0.8)*0.85;
        var points=new Point3DCollection();
        for(var row=0;row<=Rows;row++) for(var col=0;col<=Columns;col++)
        {
            var x=col*_width/Columns;var y=row*_height/Rows;
            var curlX=fromLeft?_width-x:x;
            var distance=(_width-curlX)*0.6+(_height-y)*0.8;
            var bent=Math.Max(0,depth-distance);
            var angle=Math.Min(bent/radius,Math.PI*0.94);
            var shift=radius*Math.Sin(angle)-bent;
            var bentX=curlX+shift*0.6;
            points.Add(new Point3D(fromLeft?_width-bentX:bentX,_height-(y+shift*0.8),radius*(1-Math.Cos(angle))));
        }
        _mesh.Positions=points;
    }

    public void Crumple(double progress)
    {
        progress=Math.Clamp(progress,0,1);
        var shrink=1-progress*0.88;
        var points=new Point3DCollection();
        for(var row=0;row<=Rows;row++) for(var col=0;col<=Columns;col++)
        {
            var u=(double)col/Columns;var v=(double)row/Rows;
            var folds=Math.Sin(u*31+v*17)*Math.Sin(v*29-u*13);
            var wrinkle=progress*Math.Min(_width,_height)*0.10;
            var x=_width/2+(u-0.5)*_width*shrink+Math.Sin(v*32)*wrinkle*0.25;
            var y=_height/2+(0.5-v)*_height*shrink+Math.Cos(u*28)*wrinkle*0.25;
            points.Add(new Point3D(x,y,folds*wrinkle));
        }
        _mesh.Positions=points;
    }
}
