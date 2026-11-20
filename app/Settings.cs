using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace Hesheng;
public sealed class Sound {
    public float[] Bands {get;set;}=new float[5];
    public float Preamp {get;set;}
    public bool Enabled {get;set;}=true;
    public string Preset {get;set;}="原声";
    public Sound Clone()=>new(){Bands=(float[])Bands.Clone(),Preamp=Preamp,Enabled=Enabled,Preset=Preset};
    public void Normalize(){
        if(Bands?.Length!=5)Bands=new float[5];
        for(int i=0;i<5;i++)Bands[i]=Math.Clamp(Bands[i],-12,12);
        Preamp=Math.Clamp(Preamp,-18,0);
    }
}
public sealed class Settings {
    public string Source {get;set;}="";
    public string Target {get;set;}="";
    public string Theme {get;set;}="system";
    public bool HasSeenGuide {get;set;}
    public Dictionary<string,Sound> Profiles {get;set;}=new();
    public Dictionary<string,int> Delays {get;set;}=new();
    public Dictionary<string,Sound> Presets {get;set;}=new();
    static string PathName=>Path.Combine(AppContext.BaseDirectory,"settings.json");
    public static Settings Load(out string warning){
        warning="";
        if(File.Exists(PathName)){
            try{
                var config=JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName))??throw new InvalidDataException();
                config.Profiles??=new();config.Presets??=new();config.Delays??=new();
                foreach(var s in config.Profiles.Values.Concat(config.Presets.Values))s.Normalize();
                return config;
            }catch(Exception){warning="配置无法读取，已使用初始设置。";}
        }
        var fresh=new Settings();
        string ini=Path.Combine(AppContext.BaseDirectory,"config.ini");
        if(File.Exists(ini))foreach(string line in File.ReadLines(ini)){
            var pair=line.Split('=',2);if(pair.Length!=2)continue;
            if(pair[0].Trim()=="source_device_id")fresh.Source=pair[1].Trim();
            if(pair[0].Trim()=="target_device_id")fresh.Target=pair[1].Trim();
        }
        return fresh;
    }
    public void Save(){
        File.WriteAllText(PathName+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(PathName+".tmp",PathName,true);
    }
}
