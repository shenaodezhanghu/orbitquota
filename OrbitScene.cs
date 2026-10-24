using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexIsland;
public sealed class OrbitScene : FrameworkElement
{
    private readonly DispatcherTimer clock=new() { Interval=TimeSpan.FromMilliseconds(250) };
    private readonly DateTimeOffset epoch=DateTimeOffset.UtcNow;
    private readonly EarthSurface earth=new();
    private readonly StreamGeometry solarTrack=Track(OrbitMath.Sun,0,1),lunarTrack=Track(OrbitMath.Moon,0,1);
    private Quotas? quotas;
    private bool stale,legend,reduced;
    private double speed;
    private long previousWeekReset,previousFiveReset;
    private DateTimeOffset? resetFlash;
    public DateTimeOffset? PreviewTime { get; set; }
    public bool Floating { get; set; }=true;
    public OrbitScene()
    {
        Loaded+=(_,_)=>clock.Start(); Unloaded+=(_,_)=>clock.Stop();
        clock.Tick+=(_,_)=> { if(IsVisible) InvalidateVisual(); };
    }
    public void Update(Quotas? value,double outputPerMinute,bool old,bool reducedMotion,bool showLegend)
    {
        if(value is not null&&((previousWeekReset>0&&value.Week?.Reset>previousWeekReset)||(previousFiveReset>0&&value.Five?.Reset>previousFiveReset))) resetFlash=DateTimeOffset.UtcNow;
        previousWeekReset=value?.Week?.Reset??previousWeekReset; previousFiveReset=value?.Five?.Reset??previousFiveReset;
        quotas=value;speed=outputPerMinute;stale=old;reduced=reducedMotion;legend=showLegend;
        clock.Interval=TimeSpan.FromMilliseconds(reduced?1000:250);InvalidateVisual();
    }
    private static SolidColorBrush Brush(string hex) { var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static Pen Line(string hex,double width=.6) { var p=new Pen(Brush(hex),width);p.Freeze();return p; }
    private void Text(DrawingContext dc,string value,double x,double y,double size,string color,bool center=false)
    {
        var text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new Point(center?x-text.Width/2:x,y));
    }
    private static RadialGradientBrush Glow(Color color,byte alpha)
    {
        var b=new RadialGradientBrush();b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha,color.R,color.G,color.B),0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha/3),color.R,color.G,color.B),.4));b.GradientStops.Add(new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),1));b.Freeze();return b;
    }
    private static StreamGeometry Track(Func<double,Point> point,double start,double end)
    {
        var path=new StreamGeometry();using(var g=path.Open()) { g.BeginFigure(point(start),false,false);for(int i=1;i<=90;i++)g.LineTo(point(start+(end-start)*i/90d),true,false); }path.Freeze();return path;
    }
    private static void Arrow(DrawingContext dc,Func<double,Point> orbit,string color)
    {
        var p=orbit(.78);var tangent=orbit(.782)-orbit(.778);tangent.Normalize();var normal=new Vector(-tangent.Y,tangent.X);
        dc.DrawLine(Line(color,.65),p-tangent*2.7+normal*1.6,p);dc.DrawLine(Line(color,.65),p-tangent*2.7-normal*1.6,p);
    }
    private void Sun(DrawingContext dc,double progress,double ambient,double pulse)
    {
        Point sun=OrbitMath.Sun(progress);double remaining=(quotas?.Week?.Remaining??0)/100;
        dc.PushOpacity(stale?.42:1);
        dc.DrawEllipse(Glow(Color.FromRgb(239,170,82),(byte)(65*pulse)),null,sun,21,21);
        for(int i=0;i<16;i++)
        {
            double a=i*Math.PI*2/16+ambient*.009,strength=Math.Clamp(remaining*16-i,0,1);
            var pen=new Pen(new SolidColorBrush(Color.FromArgb((byte)(30+strength*150),243,192,113)),.75);
            dc.DrawLine(pen,new Point(sun.X+7.6*Math.Cos(a),sun.Y+7.6*Math.Sin(a)),new Point(sun.X+10.2*Math.Cos(a),sun.Y+10.2*Math.Sin(a)));
        }
        var fill=Brush("#E8BD79");
        dc.DrawEllipse(fill,Line("#F7D99D",.6),sun,4.6,4.6);dc.Pop();
    }
    private void Moon(DrawingContext dc,double progress,double pulse)
    {
        Point moon=OrbitMath.Moon(progress);double r=5.2,fraction=(quotas?.Five?.Remaining??0)/100;
        dc.PushOpacity(stale?.42:1);
        dc.DrawEllipse(Glow(Color.FromRgb(173,197,236),(byte)(40*pulse)),null,moon,18,18);
        dc.DrawEllipse(Brush("#202C3E"),Line("#8194B0",.6),moon,r,r);
        var disk=new EllipseGeometry(moon,r,r);
        var right=new CombinedGeometry(GeometryCombineMode.Intersect,disk,new RectangleGeometry(new Rect(moon.X,moon.Y-r,r,r*2)));
        Geometry lit;
        if(fraction<=0)lit=Geometry.Empty;else if(fraction>=1)lit=disk;else
        {var terminator=new EllipseGeometry(moon,Math.Max(.001,Math.Abs(2*fraction-1)*r),r);lit=fraction<=.5?new CombinedGeometry(GeometryCombineMode.Exclude,right,terminator):new CombinedGeometry(GeometryCombineMode.Union,right,terminator);}
        dc.DrawGeometry(new LinearGradientBrush(Brush("#E7EDF8").Color,Brush("#AABDD9").Color,30),null,lit);
        dc.PushClip(lit);dc.DrawEllipse(Brush("#95A7C3"),null,new Point(moon.X+2.3,moon.Y-2),1.2,1.2);dc.DrawEllipse(Brush("#AFBED4"),null,new Point(moon.X-1.5,moon.Y+2.4),1.6,1.1);dc.Pop();dc.Pop();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(ActualWidth<=0||ActualHeight<=0)return;
        double scale=Math.Min(ActualWidth/140,ActualHeight/140);
        dc.PushTransform(new TranslateTransform((ActualWidth-140*scale)/2,0));
        dc.PushTransform(new ScaleTransform(scale,scale));
        var now=PreviewTime??DateTimeOffset.Now;double ambient=reduced?0:(DateTimeOffset.UtcNow-epoch).TotalSeconds,pulse=.92+.08*Math.Sin(ambient/3);
        double? sunProgress=OrbitMath.Progress(quotas?.Week,now),moonProgress=OrbitMath.Progress(quotas?.Five,now);
        if(Floating)
        {
            var veil=new RadialGradientBrush();veil.GradientStops.Add(new GradientStop(Color.FromArgb(238,8,17,30),0));veil.GradientStops.Add(new GradientStop(Color.FromArgb(220,8,17,30),.62));veil.GradientStops.Add(new GradientStop(Color.FromArgb(0,8,17,30),1));
            dc.DrawEllipse(veil,null,new Point(70,66),73,70);
        }
        (double x,double y,double r)[] stars={(17,24,.35),(37,10,.4),(112,19,.4),(122,47,.35),(12,82,.3),(109,104,.35),(33,109,.35)};
        foreach(var star in stars)dc.DrawEllipse(Brush("#62728D"),null,new Point(star.x,star.y),star.r,star.r);
        dc.DrawGeometry(null,Line("#756247",.65),solarTrack);
        dc.DrawGeometry(null,Line("#59708D",.65),lunarTrack);
        Arrow(dc,OrbitMath.Sun,"#9C7F58");Arrow(dc,OrbitMath.Moon,"#8AA8CD");
        var center=OrbitMath.Earth;const double radius=16.5;
        dc.DrawImage(earth.Get(sunProgress is double sunlight?OrbitMath.SunLight(sunlight):null),new Rect(center.X-radius,center.Y-radius,radius*2,radius*2));
        dc.DrawEllipse(null,Line("#4A7198",.55),center,radius,radius);
        if(sunProgress is double sp)Sun(dc,sp,ambient,pulse);
        if(moonProgress is double mp)Moon(dc,mp,pulse);
        if(speed>0)Text(dc,speed>=1000?(speed/1000).ToString("0.0")+"k /min":speed.ToString("0")+" /min",70,127,8,"#A6B4C8",true);
        if(legend)
        {Text(dc,quotas?.Week is null?"W —":"W "+quotas.Week.Remaining.ToString("0")+"%",18,125,7,"#CDAA71",true);Text(dc,quotas?.Five is null?"5h —":"5h "+quotas.Five.Remaining.ToString("0")+"%",122,125,7,"#A9BEDF",true);}
        if(resetFlash is DateTimeOffset flash&&!reduced)
        {double age=(DateTimeOffset.UtcNow-flash).TotalSeconds;if(age<2)dc.DrawEllipse(Glow(Color.FromRgb(186,205,231),(byte)(30*(1-age/2))),null,center,63,63);}
        dc.Pop();dc.Pop();
    }
}
