using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace CodexIsland;
// A small procedurally painted globe, cached until the solar direction visibly changes.
// Land silhouettes are stylized; this is a quota instrument, not a geographical dataset.
public sealed class EarthSurface
{
    private const int Size=112;
    private readonly byte[] pixels=new byte[Size*Size*4];
    private readonly double[] nx=new double[Size*Size],ny=new double[Size*Size],nz=new double[Size*Size],noise=new double[Size*Size],cities=new double[Size*Size];
    private readonly bool[] land=new bool[Size*Size],valid=new bool[Size*Size];
    private readonly WriteableBitmap bitmap=new(Size,Size,96,96,PixelFormats.Pbgra32,null);
    private Vector3D? previous;
    private bool initialized,previousKnown;
    private static readonly Point[][] Continents=
    {
        new[] {new Point(-17,36),new Point(8,37),new Point(33,31),new Point(43,12),new Point(51,11),new Point(42,-10),new Point(34,-24),new Point(19,-35),new Point(13,-27),new Point(10,-8),new Point(-1,5),new Point(-15,12)},
        new[] {new Point(-10,36),new Point(-9,44),new Point(-2,49),new Point(6,54),new Point(8,59),new Point(19,70),new Point(31,71),new Point(29,59),new Point(49,54),new Point(74,72),new Point(112,72),new Point(135,60),new Point(119,48),new Point(102,36),new Point(113,20),new Point(102,9),new Point(90,22),new Point(80,9),new Point(68,25),new Point(53,28),new Point(46,13),new Point(34,30),new Point(27,40),new Point(16,40),new Point(13,45),new Point(7,43),new Point(2,42),new Point(0,36)},
        new[] {new Point(-81,10),new Point(-64,11),new Point(-48,1),new Point(-35,-7),new Point(-43,-24),new Point(-55,-34),new Point(-67,-55),new Point(-75,-44),new Point(-78,-20),new Point(-72,-3)},
        new[] {new Point(-130,54),new Point(-113,71),new Point(-73,78),new Point(-54,53),new Point(-66,44),new Point(-80,25),new Point(-91,18),new Point(-106,24),new Point(-118,34)},
        new[] {new Point(-53,60),new Point(-28,71),new Point(-21,81),new Point(-48,83),new Point(-64,72)},
        new[] {new Point(-8,50),new Point(-3,50),new Point(0,58),new Point(-5,59)},
        new[] {new Point(44,-13),new Point(50,-16),new Point(47,-25),new Point(43,-24)}
    };
    public EarthSurface()
    {
        for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
        {
            int i=y*Size+x; double a=(x+.5-Size/2d)/(Size/2d),b=(Size/2d-y-.5)/(Size/2d);
            if(a*a+b*b>=1) continue;
            valid[i]=true; nx[i]=a; ny[i]=b; nz[i]=Math.Sqrt(1-a*a-b*b);
            double longitude=Math.Atan2(a,nz[i])*180/Math.PI+12,latitude=Math.Asin(b)*180/Math.PI;
            foreach(var continent in Continents) if(Inside(continent,longitude,latitude)) { land[i]=true; break; }
            noise[i]=((x*73856093L^y*19349663L)&255)/255d;
            foreach(var city in new[] {new Point(2,49),new Point(13,52),new Point(29,41),new Point(31,30),new Point(3,7),new Point(28,-26),new Point(37,56),new Point(77,28),new Point(-74,41)})
            { double dx=(longitude-city.X)*Math.Cos(latitude*Math.PI/180),dy=latitude-city.Y; cities[i]+=Math.Exp(-(dx*dx+dy*dy)/14); }
        }
    }
    private static bool Inside(Point[] polygon,double x,double y)
    {
        bool inside=false;
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            if((polygon[i].Y>y)!=(polygon[j].Y>y)&&x<(polygon[j].X-polygon[i].X)*(y-polygon[i].Y)/(polygon[j].Y-polygon[i].Y)+polygon[i].X) inside=!inside;
        return inside;
    }
    public ImageSource Get(Vector3D? sunlight)
    {
        if(initialized && sunlight.HasValue==previousKnown && (!sunlight.HasValue || (sunlight.Value-previous!.Value).Length<0.0008)) return bitmap;
        previous=sunlight; previousKnown=sunlight.HasValue; initialized=true;
        for(int i=0;i<valid.Length;i++)
        {
            if(!valid[i]) continue;
            double dot=sunlight is Vector3D l?nx[i]*l.X+ny[i]*l.Y:0.2;
            double twilight=Math.Clamp((dot+.055)/.11,0,1); twilight=twilight*twilight*(3-2*twilight);
            double illumination=sunlight.HasValue?.16+.74*twilight:.3;
            double variation=.98+noise[i]*.04;
            double red=(land[i]?68:24)*illumination*variation,green=(land[i]?116:89)*illumination*variation,blue=(land[i]?99:154)*illumination*variation;
            if(sunlight.HasValue)
            {
                double lamp=land[i]?Math.Min(1,cities[i])*(1-twilight)*(noise[i]>.66?1:.13):0;
                red+=lamp*200; green+=lamp*130; blue+=lamp*48;
            }
            byte alpha=(byte)(255*Math.Clamp(nz[i]*14,0,1));
            pixels[i*4]=(byte)(Math.Clamp(blue,0,255)*alpha/255);pixels[i*4+1]=(byte)(Math.Clamp(green,0,255)*alpha/255);pixels[i*4+2]=(byte)(Math.Clamp(red,0,255)*alpha/255);pixels[i*4+3]=alpha;
        }
        bitmap.WritePixels(new Int32Rect(0,0,Size,Size),pixels,Size*4,0); return bitmap;
    }
}
