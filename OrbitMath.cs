using System;
using System.Windows;
using System.Windows.Media.Media3D;

namespace CodexIsland;
public static class OrbitMath
{
    // Position describes wall-clock quota-window progress, not the percentage consumed.
    public static double? Progress(WindowQuota? window,DateTimeOffset now)
    {
        if(window is null||window.Reset<=0||window.Minutes<=0) return null;
        double left=window.Reset-now.ToUnixTimeMilliseconds()/1000d;
        return Math.Clamp(1-left/(window.Minutes*60d),0,1);
    }
    public static readonly Point Earth=new(70,62);
    public static Point Sun(double p)=>Project(p,48);
    public static Point Moon(double p)=>Project(p,31);
    private static Point Project(double p,double radius)
    {
        double angle=2*Math.PI*Math.Clamp(p,0,1)-Math.PI/2;
        return new(Earth.X+radius*Math.Cos(angle),Earth.Y+radius*Math.Sin(angle));
    }
    public static Vector3D SunLight(double p)
    {
        var projected=Sun(p);
        // Flat, top-down light direction. There is deliberately no depth or perspective.
        var light=new Vector3D(projected.X-Earth.X,-(projected.Y-Earth.Y),0);
        light.Normalize(); return light;
    }
}
