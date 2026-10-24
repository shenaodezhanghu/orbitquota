using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexIsland;
public sealed class QuotaRing : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty=DependencyProperty.Register(nameof(Percent),typeof(double),typeof(QuotaRing),new FrameworkPropertyMetadata(-1d,FrameworkPropertyMetadataOptions.AffectsRender));
    public double Percent { get=>(double)GetValue(PercentProperty); set=>SetValue(PercentProperty,value); }
    public string Label { get; set; }="W";
    public Color Accent { get; set; }=Color.FromRgb(119,232,187);
    public void Update(double? remaining,bool reduced)
    {
        double target=remaining??-1;
        if(Math.Abs(target-Percent)<0.01) return;
        if(reduced || Percent<0 || target<0) { BeginAnimation(PercentProperty,null); Percent=target; }
        else BeginAnimation(PercentProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(600)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight,thickness=w>40?4:3;
        double radius=Math.Min(w,h)/2-thickness-1; Point center=new(w/2,h/2);
        dc.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromRgb(49,57,72)),thickness),center,radius,radius);
        Color color=Percent>=0 && Percent<15?Color.FromRgb(255,129,137):Percent>=0 && Percent<35?Color.FromRgb(245,196,111):Accent;
        if(Percent>0)
        {
            double angle=Math.Min(Percent,99.999)/100*Math.PI*2;
            Point start=new(center.X,center.Y-radius),end=new(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle));
            var geo=new StreamGeometry();
            using(var ctx=geo.Open()) { ctx.BeginFigure(start,false,false); ctx.ArcTo(end,new Size(radius,radius),0,angle>Math.PI,SweepDirection.Clockwise,true,false); }
            geo.Freeze();
            var pen=new Pen(new SolidColorBrush(color),thickness) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round };
            dc.DrawGeometry(null,pen,geo);
        }
        var text=new FormattedText(Label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),w>40?12:8.5,new SolidColorBrush(Percent<0?Colors.Gray:color),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new Point((w-text.Width)/2,(h-text.Height)/2));
    }
}
