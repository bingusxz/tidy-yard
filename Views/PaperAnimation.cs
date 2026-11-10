using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TidyYard.Views;

/// <summary>A textured sheet drawn as a mesh: curled while it is stuck to the pad, then drawn from the physics once it is free.</summary>
public sealed class PaperAnimation : Viewport3D
{
    private const int Columns = 32;
    private const int Rows = 40;

    /// <summary>The physics grid is every second node of the drawn mesh; the mesh between is smoothed from it.</summary>
    public const int SimColumns = Columns / 2;
    public const int SimRows = Rows / 2;

    private readonly MeshGeometry3D _mesh;
    private readonly MeshGeometry3D _shadow;
    private readonly double _width;
    private readonly double _height;
    private double _viewWidth;
    private double _viewHeight;
    public PaperAnimation(ImageSource image, Color paper, double width, double height)
    {
        _width = Math.Max(1,width); _height = Math.Max(1,height);
        _viewWidth = _width; _viewHeight = _height;
        ClipToBounds = false; IsHitTestVisible = false;
        Camera = new OrthographicCamera(new Point3D(_width/2,_height/2,1000),new Vector3D(0,0,-1),new Vector3D(0,1,0),_width);
        _mesh = new MeshGeometry3D();
        _shadow = new MeshGeometry3D();
        for(var row=0;row<=Rows;row++) for(var col=0;col<=Columns;col++)
        {
            _mesh.Positions.Add(new Point3D(col*_width/Columns,_height-row*_height/Rows,0));
            _mesh.TextureCoordinates.Add(new Point((double)col/Columns,(double)row/Rows));
        }
        for(var row=0;row<Rows;row++) for(var col=0;col<Columns;col++)
        {
            var a=row*(Columns+1)+col;var b=a+1;var c=a+Columns+1;var d=c+1;
            // Counterclockwise front faces, with the texture's top at the sheet's top.
            foreach(var i in new[]{a,c,b,b,c,d}) { _mesh.TriangleIndices.Add(i); _shadow.TriangleIndices.Add(i); }
        }
        for(var i=0;i<_mesh.Positions.Count;i++) _shadow.Positions.Add(_mesh.Positions[i]);
        var lights = new Model3DGroup();
        // The sheet's shadow on the desk: only visible once part of it is lifted.
        lights.Children.Add(new GeometryModel3D(_shadow,new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(52,0,0,0)))));
        lights.Children.Add(new AmbientLight(Color.FromRgb(180,180,180)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(95,95,95),new Vector3D(-0.4,-0.6,-1)));
        lights.Children.Add(new GeometryModel3D(_mesh,new DiffuseMaterial(new ImageBrush(image)))
        { BackMaterial = new DiffuseMaterial(new SolidColorBrush(paper)) });
        Children.Add(new ModelVisual3D { Content=lights });
    }

    /// <summary>
    /// Makes the drawing area larger than the sheet, so a sheet carried around can swing outside its own rectangle.
    /// The surface must then be the only content of a window of exactly this size.
    /// </summary>
    public void SetViewport(double width, double height)
    {
        _viewWidth = width; _viewHeight = height;
        Width = width; Height = height;
        Camera = new OrthographicCamera(new Point3D(width/2,height/2,1000),new Vector3D(0,0,-1),new Vector3D(0,1,0),width);
    }

    /// <summary>The current shape at the physics nodes, in sheet coordinates (x right, y down from the top-left, z up).</summary>
    public (double[] X, double[] Y, double[] Z) Snapshot()
    {
        var count = (SimColumns+1)*(SimRows+1);
        var xs = new double[count]; var ys = new double[count]; var zs = new double[count];
        for(var r=0;r<=SimRows;r++) for(var c=0;c<=SimColumns;c++)
        {
            var p = _mesh.Positions[(r*2)*(Columns+1)+c*2];
            var i = r*(SimColumns+1)+c;
            xs[i]=p.X; ys[i]=_height-p.Y; zs[i]=p.Z;
        }
        return (xs,ys,zs);
    }

    /// <summary>Which physics nodes are still stuck to the pad at this curl: the ones the curl has not lifted yet.</summary>
    public bool[] StuckNodes(double progress, bool fromLeft)
    {
        progress=Math.Clamp(progress,0,1);
        var depth=progress*(_width*0.6+_height*0.8)*0.85;
        var stuck = new bool[(SimColumns+1)*(SimRows+1)];
        for(var r=0;r<=SimRows;r++) for(var c=0;c<=SimColumns;c++)
        {
            var x=c*2*_width/Columns;var y=r*2*_height/Rows;
            var curlX=fromLeft?_width-x:x;
            var distance=(_width-curlX)*0.6+(_height-y)*0.8;
            stuck[r*(SimColumns+1)+c]=depth-distance<=0.5;
        }
        return stuck;
    }

    /// <summary>
    /// Draws the simulated sheet. Positions are world coordinates; (originX, originY) is the world point at the
    /// top-left of the drawing area. The drawn mesh is smoothed between the physics nodes.
    /// </summary>
    public void Render(double[] x, double[] y, double[] z, double originX, double originY)
    {
        var stride = SimColumns+1;
        double Pick(double[] a,int c,int r) => a[Math.Clamp(r,0,SimRows)*stride+Math.Clamp(c,0,SimColumns)];
        static double Cubic(double p0,double p1,double p2,double p3) => 0.0625*(-p0+9*p1+9*p2-p3); // Catmull-Rom at t = 0.5
        // Value of channel a at physics coordinate (c + fc/2, r + fr/2): fc, fr are 0 (on a node) or 1 (halfway to the next).
        double Sample(double[] a,int c,int r,int fc,int fr)
        {
            if(fc==0&&fr==0) return Pick(a,c,r);
            if(fr==0) return Cubic(Pick(a,c-1,r),Pick(a,c,r),Pick(a,c+1,r),Pick(a,c+2,r));
            if(fc==0) return Cubic(Pick(a,c,r-1),Pick(a,c,r),Pick(a,c,r+1),Pick(a,c,r+2));
            var row0=Cubic(Pick(a,c-1,r-1),Pick(a,c,r-1),Pick(a,c+1,r-1),Pick(a,c+2,r-1));
            var row1=Cubic(Pick(a,c-1,r),Pick(a,c,r),Pick(a,c+1,r),Pick(a,c+2,r));
            var row2=Cubic(Pick(a,c-1,r+1),Pick(a,c,r+1),Pick(a,c+1,r+1),Pick(a,c+2,r+1));
            var row3=Cubic(Pick(a,c-1,r+2),Pick(a,c,r+2),Pick(a,c+1,r+2),Pick(a,c+2,r+2));
            return Cubic(row0,row1,row2,row3);
        }
        var points=new Point3DCollection(); var shade=new Point3DCollection();
        for(var row=0;row<=Rows;row++) for(var col=0;col<=Columns;col++)
        {
            var c=col/2;var r=row/2;var fc=col%2;var fr=row%2;
            var px=Sample(x,c,r,fc,fr)-originX;
            var py=_viewHeight-(Sample(y,c,r,fc,fr)-originY);
            var pz=Math.Max(0,Sample(z,c,r,fc,fr));
            points.Add(new Point3D(px,py,pz));
            // Light from the upper left: height throws the shadow down and to the right on the desk.
            shade.Add(new Point3D(px+pz*0.35,py-pz*0.35,-2));
        }
        _mesh.Positions=points;
        _shadow.Positions=shade;
    }

    private const double RollCore = 3.0;        // radius of the very first turn, px
    private const double RollThickness = 0.11;  // how much the radius grows per radian: the paper's thickness

    /// <summary>
    /// Rolls the sheet up from its bottom edge into a scroll, like paper wound round a pencil. The outer turn
    /// stays tangent to the flat sheet; each turn inside it is smaller. Returns the lowest y (down) the sheet reaches.
    /// The finished scroll sits just below the sheet's top edge, so a header above it stays visible.
    /// </summary>
    public double Roll(double progress)
    {
        progress=Math.Clamp(progress,0,1);
        var rolled=progress*_height;                 // length of paper wound up
        double Turn(double u)=>(-RollCore+Math.Sqrt(RollCore*RollCore+2*RollThickness*u))/RollThickness;
        double Radius(double u)=>RollCore+RollThickness*Turn(u);
        var turnOuter=Turn(rolled);var radiusOuter=Radius(rolled);
        var flatEnd=_height-rolled;
        // Eases the finished scroll down so it hangs under the header instead of overlapping it.
        var push=progress*progress*(3-2*progress)*(radiusOuter+1);
        var centreY=flatEnd+push;
        var lowest=0.0;
        var rowY=new double[Rows+1];var rowZ=new double[Rows+1];
        for(var row=0;row<=Rows;row++)
        {
            var y=row*_height/Rows;var u=_height-y;
            if(u>rolled&&flatEnd>1e-6){rowY[row]=y*(flatEnd+push)/flatEnd;rowZ[row]=0;}
            else
            {
                // Counter-clockwise round the scroll's axis, starting at the bottom of the outer turn.
                var angle=-Math.PI/2+(turnOuter-Turn(u));var radius=Radius(u);
                rowY[row]=centreY+radius*Math.Cos(angle);
                rowZ[row]=radiusOuter+radius*Math.Sin(angle);
            }
            lowest=Math.Max(lowest,rowY[row]);
        }
        ApplyRows(rowY,rowZ);
        return lowest;
    }

    /// <summary>Puts the sheet at the given height (row y, down) and lift (row z, up) for every mesh row, and moves its shadow.</summary>
    private void ApplyRows(double[] rowY,double[] rowZ)
    {
        var points=new Point3DCollection();var shade=new Point3DCollection();
        for(var row=0;row<=Rows;row++) for(var col=0;col<=Columns;col++)
        {
            var x=col*_width/Columns;var py=_height-rowY[row];var pz=Math.Max(0,rowZ[row]);
            points.Add(new Point3D(x,py,pz));
            shade.Add(new Point3D(x+pz*0.35,py-pz*0.35,-2));
        }
        _mesh.Positions=points;_shadow.Positions=shade;
    }

    /// <summary>
    /// Folds the sheet up like an accordion under its top edge, bottom panel first. Each fold is a smooth hinge, not a
    /// sharp crease, and the finished pleats are left slightly open. Returns the lowest y (down) the sheet reaches.
    /// </summary>
    public double Fold(double progress,int panels=4)
    {
        progress=Math.Clamp(progress,0,1);
        var open=Math.PI*0.93;                        // a finished fold stops just short of lying flat
        var panelLength=_height/panels;const double hinge=10;
        var stage=new double[panels];
        for(var k=1;k<panels;k++)
        {
            var start=(panels-1-k)*0.5/(panels-1);   // the hinge nearest the free edge folds first
            var t=Math.Clamp((progress-start)/0.5,0,1);stage[k]=t*t*(3-2*t);
        }
        double Heading(double s)
        {
            double angle=0;
            for(var k=1;k<panels;k++)
            {
                var x=Math.Clamp((s-k*panelLength)/hinge+0.5,0,1);
                angle+=(k%2==1?1:-1)*open*stage[k]*(x*x*(3-2*x));
            }
            return angle;
        }
        var rowY=new double[Rows+1];var rowZ=new double[Rows+1];
        const int sub=6;var step=_height/Rows/sub;double y=0,z=0,along=0,lowest=0;
        for(var row=1;row<=Rows;row++)
        {
            for(var i=0;i<sub;i++){var angle=Heading(along+step/2);along+=step;y+=Math.Cos(angle)*step;z+=Math.Sin(angle)*step;}
            rowY[row]=y;rowZ[row]=z;lowest=Math.Max(lowest,y);
        }
        ApplyRows(rowY,rowZ);
        return lowest;
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

}
