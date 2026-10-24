using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexIsland;
public sealed class QuotaArc : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty=DependencyProperty.Register(nameof(Percent),typeof(double),typeof(QuotaArc),new FrameworkPropertyMetadata(-1d,FrameworkPropertyMetadataOptions.AffectsRender));
    public double Percent { get=>(double)GetValue(PercentProperty); set=>SetValue(PercentProperty,value); }
    public bool RightSide { get; set; }
    public void Update(double? remaining,bool reduced)
    {
        double target=remaining??-1;
        if(Math.Abs(target-Percent)<0.01) return;
        if(reduced||Percent<0||target<0) { BeginAnimation(PercentProperty,null); Percent=target; }
        else BeginAnimation(PercentProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(500)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
    }
    protected override void OnRender(DrawingContext dc)
    {
        double rx=Math.Max(1,ActualWidth-3),ry=Math.Max(1,ActualHeight/2-2);
        Point center=new(RightSide?1:ActualWidth-1,ActualHeight/2);
        void Arc(double percent,Brush color,double thickness)
        {
            if(percent<=0) return;
            double sweep=150*percent/100;
            double a=(RightSide?-75:105)*Math.PI/180,b=a+sweep*Math.PI/180;
            Point start=new(center.X+rx*Math.Cos(a),center.Y+ry*Math.Sin(a));
            Point end=new(center.X+rx*Math.Cos(b),center.Y+ry*Math.Sin(b));
            var path=new StreamGeometry(); using(var ctx=path.Open()) { ctx.BeginFigure(start,false,false); ctx.ArcTo(end,new Size(rx,ry),0,false,SweepDirection.Clockwise,true,false); }
            path.Freeze(); dc.DrawGeometry(null,new Pen(color,thickness) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round },path);
        }
        Arc(100,new SolidColorBrush(Color.FromRgb(46,53,63)),2.6);
        Color color=Percent<15?Color.FromRgb(229,144,145):RightSide?Color.FromRgb(166,181,235):Color.FromRgb(156,217,191);
        Arc(Percent,new SolidColorBrush(color),3.2);
    }
}
