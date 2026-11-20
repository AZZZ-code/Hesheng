using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
namespace Hesheng;
public class DriverArt : FrameworkElement {
    static Color C(string value)=>(Color)ColorConverter.ConvertFromString(value);
    static Brush Radial(params (double offset,string color)[] stops){
        var brush=new RadialGradientBrush {GradientOrigin=new Point(.43,.36), Center=new Point(.50,.50), RadiusX=.50, RadiusY=.50};
        foreach(var s in stops)brush.GradientStops.Add(new GradientStop(C(s.color),s.offset));
        brush.Freeze();return brush;
    }
    static Brush Metal(string light,string dark){
        var brush=new LinearGradientBrush(C(light),C(dark),38);brush.Freeze();return brush;
    }
    protected override void OnRender(DrawingContext d){
        var center=new Point(ActualWidth*.55,ActualHeight*.53);
        double r=Math.Min(ActualWidth*.46,ActualHeight*.50);
        void Disc(double scale,Brush brush){d.DrawEllipse(brush,null,center,r*scale,r*scale);}
        Disc(1,Metal("#92918B","#151618"));
        Disc(.987,Radial((0,"#1C1D1F"),(.88,"#27282A"),(.97,"#101112"),(1,"#484949")));
        Disc(.949,Metal("#94948C","#252628"));
        Disc(.936,Radial((0,"#27292B"),(.73,"#121315"),(.83,"#47494A"),(.91,"#363739"),(.99,"#111215")));
        Disc(.788,Radial((0,"#757878"),(.28,"#444748"),(.5,"#303335"),(.91,"#4B4E4F"),(1,"#17191A")));
        for(int i=0;i<38;i++){
            double size=r*(.34+i*.0115);
            d.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb((byte)(i%2==0?36:18),0,0,0)),.7),center,size,size);
        }
        Disc(.32,Metal("#18191B","#858786"));Disc(.309,Radial((0,"#626564"),(.43,"#474A4A"),(1,"#191B1E")));
        for(int i=0;i<6;i++){
            double a=(i+.5)*Math.PI/3;
            var p=new Point(center.X+r*.97*Math.Cos(a),center.Y+r*.97*Math.Sin(a));
            d.DrawEllipse(Metal("#111214","#636461"),null,p,1.9,1.9);
        }
    }
}
public class ResponsePlot:FrameworkElement {
    public float[] Gains=new float[5]; public double Preamp;public int Rate=48000;public bool EnabledEq=true;
    static readonly double[] Centers={80,250,1000,4000,10000};
    protected override void OnRender(DrawingContext d){
        double w=ActualWidth,h=ActualHeight-18;if(w<=30||h<10)return;
        var dim=new SolidColorBrush(Color.FromRgb(145,149,150));
        var gridPen=new Pen(new SolidColorBrush(Color.FromRgb(58,61,63)),.7);
        double X(double hz)=>Math.Log(hz/30)/Math.Log(20000.0/30)*w;
        double Y(double db)=>h*(.5-Math.Clamp(db,-18,18)/36);
        foreach(var db in new[]{-12,0,12}) d.DrawLine(gridPen,new Point(0,Y(db)),new Point(w,Y(db)));
        foreach(double hz in Centers){
            double x=X(hz);d.DrawLine(gridPen,new Point(x,3),new Point(x,h));
            var label=new FormattedText(hz>=1000?(hz/1000)+"k":hz.ToString(),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,dim,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            d.DrawText(label,new Point(x-label.Width/2,h+2));
        }
        var curve=new StreamGeometry();using(var ctx=curve.Open()){
            for(int i=0;i<=180;i++){
                double f=30*Math.Pow(20000.0/30,i/180.0), db=EnabledEq?Preamp:0;
                Complex z=Complex.FromPolarCoordinates(1,-2*Math.PI*Math.Min(f,Rate*.499)/Rate);
                if(EnabledEq) for(int j=0;j<5;j++){
                    double angle=2*Math.PI*Math.Min(Centers[j],Rate*.44)/Rate;
                    double a=Math.Pow(10,Gains[j]/40.0), alpha=Math.Sin(angle)/1.7, b=-2*Math.Cos(angle);
                    db+=20*Math.Log10(((1+alpha*a+b*z+(1-alpha*a)*z*z)/(1+alpha/a+b*z+(1-alpha/a)*z*z)).Magnitude);
                }
                var p=new Point(w*i/180,Y(db));
                if(i==0)ctx.BeginFigure(p,false,false);else ctx.LineTo(p,true,false);
            }
        }
        d.DrawGeometry(null,new Pen(new SolidColorBrush(EnabledEq?Color.FromRgb(238,196,131):Color.FromRgb(150,150,150)),2.5),curve);
    }
}
