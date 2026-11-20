using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace Hesheng;
public record Device(string Id,string Name,bool Preferred) {
    public string Label=>Preferred?Name+"（系统默认）":Name;
    public override string ToString()=>Label;
    public string ShortName {
        get {
            int start=Name.IndexOf('('), end=Name.LastIndexOf(')');
            string value=start>=0&&end>start&&Name.StartsWith("扬声器")?Name[(start+1)..end]:Name;
            return System.Text.RegularExpressions.Regex.Replace(value,@"^\d+-\s*","");
        }
    }
}
internal static class Audio {
    const string Lib="RouteEngine.dll";
    [DllImport(Lib)] static extern int scan();
    [DllImport(Lib)] static extern IntPtr device_name(int i);
    [DllImport(Lib)] static extern IntPtr device_id(int i);
    [DllImport(Lib)] static extern int is_default(int i);
    [DllImport(Lib,CharSet=CharSet.Unicode,ExactSpelling=true)] public static extern int begin_route(string source,string target);
    [DllImport(Lib)] public static extern void stop_route();
    [DllImport(Lib)] public static extern int state();
    [DllImport(Lib)] public static extern float take_peak();
    [DllImport(Lib)] public static extern int sample_rate();
    [DllImport(Lib)] public static extern int shutdown_route();
    [DllImport(Lib)] public static extern void tune(float[] bands,float preamp,int enabled,int delay);
    [DllImport(Lib,CharSet=CharSet.Unicode,ExactSpelling=true)] public static extern int play_chime(string source);
    public static List<Device> Scan(){
        int n=scan();if(n<0)throw new InvalidOperationException("Windows 音频服务暂时无法枚举设备。");
        var devices=new List<Device>();
        for(int i=0;i<n;i++) devices.Add(new(Marshal.PtrToStringUni(device_id(i))!,Marshal.PtrToStringUni(device_name(i))!,is_default(i)!=0));
        return devices;
    }
}
