using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
namespace Hesheng;

public partial class MainWindow : Window {
    static readonly Dictionary<string,Sound> Factory=new(){
        ["原声"]=new(), ["人声"]=new(){Bands=new float[]{-2,-1,2,3,0},Preamp=-3},
        ["低音"]=new(){Bands=new float[]{5,2,0,-1,0},Preamp=-5}, ["夜间"]=new(){Bands=new float[]{-6,-3,1,0,-2},Preamp=-1}
    };
    readonly Settings config;
    readonly Slider[] bands=new Slider[5];
    readonly TextBlock[] values=new TextBlock[5];
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    readonly Forms.NotifyIcon tray=new(){Text="合声 · 双音箱控制台"};
    Sound sound=new();
    Mutex? routeLock;
    bool updating=true,scanning,testing,ready,quitting,dark;
    bool switching;
    int lastState=-1;
    string statusMessage="";
    string Pair=>config.Source+"|"+config.Target;
    Device? Primary=>Source.SelectedItem as Device;
    Device? Secondary=>Target.SelectedItem as Device;
    bool SelectionChangedFromRoute=>Primary?.Id!=config.Source||Secondary?.Id!=config.Target;
    bool ValidPair=>Primary!=null&&Secondary!=null&&Primary.Id!=Secondary.Id;
    public MainWindow(){
        InitializeComponent();
        config=Settings.Load(out statusMessage);
        BuildFaders();ApplyTheme();SelectPage(false,false);
        saveTimer.Tick+=(_,_)=>{saveTimer.Stop();Save();};
        timer.Tick+=(_,_)=>UpdateTransport();Loaded+=OnLoaded;
        tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("打开合声",null,(_,_)=>RestoreWindow());
        menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Close));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>RestoreWindow();
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized){tray.Visible=true;Hide();}};
        Closing+=OnClosing;
        SystemEvents.UserPreferenceChanged+=SystemThemeChanged;
    }
    internal void RestoreWindow()=>Dispatcher.Invoke(()=>{Show();WindowState=WindowState.Normal;Activate();tray.Visible=false;});
    async void OnLoaded(object sender,RoutedEventArgs e){
        Loaded-=OnLoaded;
        string savedSource=config.Source;
        await Reload(); if(quitting)return;
        ready=true;updating=false;timer.Start();UpdateTransport();
        if(!config.HasSeenGuide)ShowGuide(0);
        if(Environment.GetCommandLineArgs().Contains("--play")&&Primary?.Id==savedSource)Start();
    }
    async Task Reload(){
        if(scanning)return;
        string sourceId=Primary?.Id??config.Source,targetId=Secondary?.Id??config.Target;
        scanning=true;UpdateTransport();
        try {
            var devices=await Task.Run(Audio.Scan);if(quitting)return;
            updating=true;
            Source.ItemsSource=devices;Target.ItemsSource=devices;
            var saved=devices.FirstOrDefault(d=>d.Id==sourceId);
            var suggested=devices.FirstOrDefault(d=>d.Preferred)??devices.FirstOrDefault();
            Source.SelectedItem=saved??(routeLock==null?suggested:null);
            Target.SelectedItem=devices.FirstOrDefault(d=>d.Id==targetId);
            if(routeLock==null){
                if(Primary!=null)config.Source=Primary.Id;
                LoadSound();
            }
            statusMessage=devices.Count switch{
                0=>"Windows 未发现可用的播放设备。请连接音箱后刷新。",
                1=>"已识别一个播放设备。请连接第二台音箱或耳机后刷新。",
                _ when saved==null&&sourceId!=""=>"原主音箱已离线，请确认当前设备组合。",
                _ when Primary==null||Secondary==null=>"请选择可用的主音箱和副音箱。",
                _=>""
            };
            UpdateGuideHint();
        }catch(Exception ex){statusMessage="设备读取失败 · "+ex.GetBaseException().Message;}
        finally {scanning=false;updating=false;UpdateTransport();}
    }
    void BuildFaders(){
        string[] labels={"80 Hz","250 Hz","1 kHz","4 kHz","10 kHz"};
        string[] names={"低频","中低频","中频","中高频","高频"};
        for(int i=0;i<5;i++){
            var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Stretch};
            stack.Children.Add(new TextBlock{Text=labels[i],FontSize=16,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center});
            var desc=new TextBlock{Text=names[i],Style=(Style)FindResource("Caption"),HorizontalAlignment=HorizontalAlignment.Center};
            stack.Children.Add(desc);
            var slider=new Slider{Orientation=Orientation.Vertical,Minimum=-12,Maximum=12,TickFrequency=.5,IsSnapToTickEnabled=true,Height=102,Margin=new Thickness(0,6,0,3),HorizontalAlignment=HorizontalAlignment.Center};
            AutomationProperties.SetName(slider,labels[i]+" 增益");slider.ValueChanged+=BandChanged;
            stack.Children.Add(slider); bands[i]=slider;
            values[i]=new TextBlock{HorizontalAlignment=HorizontalAlignment.Center,FontFamily=new FontFamily("Consolas"),FontSize=13};
            stack.Children.Add(values[i]);Faders.Children.Add(stack);
        }
    }
    void DeviceChanged(object sender,SelectionChangedEventArgs e){
        if(updating||!ready||switching)return;
        statusMessage="";
        if(routeLock==null)CommitSelection();
        UpdateTransport();
    }
    void CommitSelection(){
        config.Source=Primary?.Id??"";config.Target=Secondary?.Id??"";
        LoadSound();ChangeSaved();
    }
    // Keep the session mutex until the previous native worker has finished
    // stopping. A second instance cannot take over between the two routes.
    async void ApplySelection(object sender,RoutedEventArgs e){
        if(switching||scanning||testing||routeLock==null||!ValidPair||!SelectionChangedFromRoute)return;
        string source=Primary!.Id,target=Secondary!.Id;
        switching=true;statusMessage="正在切换输出设备…";UpdateTransport();
        try{
            Audio.stop_route();
            if(!await WaitForRouteState(0)) {
                statusMessage="旧设备尚未释放，请稍后重试。";return;
            }
            if(quitting)return;
            // Apply the new output's stored tuning before it receives samples.
            Sound next=config.Profiles.TryGetValue(target,out var profile)?profile:new();
            int delay=config.Delays.GetValueOrDefault(source+"|"+target);
            Audio.tune(next.Bands,next.Preamp,next.Enabled?1:0,delay);
            if(Audio.begin_route(source,target)!=0 && await WaitForRouteState(2)){
                if(quitting)return;
                CommitSelection();statusMessage="已切换输出设备。";return;
            }
            if(quitting)return;
            // If the new endpoint was unplugged or busy, recover the last
            // committed pair. Do not persist a failed selection.
            Audio.stop_route();
            if(!await WaitForRouteState(0)||quitting)return;
            updating=true;
            Source.SelectedItem=Source.Items.OfType<Device>().FirstOrDefault(d=>d.Id==config.Source);
            Target.SelectedItem=Target.Items.OfType<Device>().FirstOrDefault(d=>d.Id==config.Target);
            updating=false;LoadSound();
            bool restored=Audio.begin_route(config.Source,config.Target)!=0 && await WaitForRouteState(2);
            statusMessage=restored?"新设备无法连接，已恢复原设备。":"设备连接失败，请刷新设备后重试。";
        }finally{switching=false;if(!quitting)UpdateTransport();}
    }
    async Task<bool> WaitForRouteState(int wanted){
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        while(!quitting){
            int st=Audio.state();
            if(st==wanted||(wanted==0&&st==5))return true;
            if(st==5||elapsed.ElapsedMilliseconds>8000)return false;
            await Task.Delay(50);
        }
        return false;
    }
    void LoadSound(){
        sound=config.Profiles.TryGetValue(config.Target,out var profile)?profile.Clone():new();
        updating=true;
        Delay.Value=config.Delays.TryGetValue(Pair,out int offset)?Math.Clamp(offset,0,300):0;
        DelayText.Text=$"{Delay.Value:0} ms";
        ApplyControls();updating=false;
        EqTarget.Text=Secondary?.ShortName??"请选择副音箱";
        RouteText.Text=(Primary?.ShortName??"选择主音箱")+"   →   "+(Secondary?.ShortName??"选择副音箱");
        RouteText.ToolTip=RouteText.Text;
    }
    void ApplyControls(){
        bool previous=updating;updating=true;
        for(int i=0;i<5;i++)bands[i].Value=sound.Bands[i];
        Pre.Value=sound.Preamp;updating=previous;
        RenderSound();RebuildPresets();
    }
    void RenderSound(){
        for(int i=0;i<5;i++) values[i].Text=Db(sound.Bands[i]);
        PreText.Text=Db(sound.Preamp);
        Bypass.Content=sound.Enabled?"效果已开启":"效果已旁通";
        Plot.Gains=sound.Bands;Plot.Preamp=sound.Preamp;Plot.EnabledEq=sound.Enabled;Plot.Rate=Audio.sample_rate();Plot.InvalidateVisual();
        Audio.tune(sound.Bands,sound.Preamp,sound.Enabled?1:0,(int)Delay.Value);
    }
    static string Db(double v)=>v.ToString("+0.#;-0.#;0")+" dB";
    void RebuildPresets(){
        PresetBar.Children.Clear();
        foreach(var name in Factory.Keys.Concat(config.Presets.Keys)){
            bool selected=name==sound.Preset;
            var b=new Button{Content=name,Padding=new Thickness(14,6,14,6),Margin=new Thickness(0,0,6,6)};
            b.SetResourceReference(BackgroundProperty,selected?"Ink":"Pill");
            b.SetResourceReference(ForegroundProperty,selected?"Paper":"Ink");
            b.Click+=(_,_)=>SelectPreset(name);
            PresetBar.Children.Add(b);
        }
    }
    void SelectPreset(string name){
        sound=(Factory.TryGetValue(name,out var s)?s:config.Presets[name]).Clone();
        sound.Preset=name;sound.Enabled=true;ApplyControls();StoreSound();
    }
    void StoreSound(){
        if(!string.IsNullOrEmpty(config.Target))config.Profiles[config.Target]=sound.Clone();
        ChangeSaved();
    }
    void BandChanged(object sender,RoutedPropertyChangedEventArgs<double> e){
        if(updating||!ready)return;
        for(int i=0;i<5;i++)sound.Bands[i]=(float)bands[i].Value;
        sound.Preamp=(float)Pre.Value;
        if(sound.Preset!=""){sound.Preset="";RebuildPresets();}
        RenderSound();StoreSound();
    }
    void SavePreset(object sender,RoutedEventArgs e){
        string name=PresetName.Text.Trim();
        if(name==""||Factory.ContainsKey(name)){statusMessage="请填写一个不同于出厂预设的名称。";PresetName.Focus();return;}
        if(config.Presets.ContainsKey(name)&&MessageBox.Show("覆盖已保存的「"+name+"」？","保存预设",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        sound.Preset=name;config.Presets[name]=sound.Clone();PresetName.Clear();
        RebuildPresets();StoreSound();statusMessage="已保存「"+name+"」。";
    }
    void ResetEq(object s,RoutedEventArgs e)=>SelectPreset("原声");
    void ToggleEq(object s,RoutedEventArgs e){sound.Enabled=!sound.Enabled;RenderSound();StoreSound();}
    void DelayChanged(object s,RoutedPropertyChangedEventArgs<double> e){
        if(updating||!ready)return;DelayText.Text=$"{Delay.Value:0} ms";
        config.Delays[Pair]=(int)Delay.Value;RenderSound();ChangeSaved();
    }
    void ChangeSaved(){saveTimer.Stop();saveTimer.Start();}
    void Save(){try{config.Save();}catch(IOException ex){statusMessage="设置保存失败 · "+ex.Message;}catch(UnauthorizedAccessException){statusMessage="当前目录不可写，设置未保存。";}}
    void Start(){
        if(scanning||Primary==null||Secondary==null||Primary.Id==Secondary.Id)return;
        // The upstream tray application uses this session mutex throughout its lifetime.
        // Owning the same name prevents either UI from starting a second route.
        routeLock=new Mutex(true,@"Local\WASAPI Bridge",out bool first);
        if(!first){routeLock.Dispose();routeLock=null;statusMessage="请先从托盘退出旧版 WASAPI Bridge，再开始镜像。";UpdateTransport();return;}
        statusMessage="";
        if(Audio.begin_route(Primary.Id,Secondary.Id)==0){ReleaseRoute();statusMessage="音频引擎未能启动。";}
        UpdateTransport();
    }
    void ReleaseRoute(){if(routeLock==null)return;routeLock.ReleaseMutex();routeLock.Dispose();routeLock=null;}
    void ToggleRoute(object s,RoutedEventArgs e){
        int state=Audio.state();if(state==0||state==5)Start();else Audio.stop_route();UpdateTransport();
    }
    async void TestSound(object s,RoutedEventArgs e){
        if(testing||Primary==null)return;
        string source=routeLock==null?Primary.Id:config.Source;testing=true;UpdateTransport();
        try{statusMessage=await Task.Run(()=>Audio.play_chime(source))!=0?"三音试听已播放。":"测试音播放失败，请检查主音箱连接。";}
        finally{testing=false;if(!quitting)UpdateTransport();}
    }
    async void RefreshDevices(object s,RoutedEventArgs e)=>await Reload();
    void UpdateTransport(){
        if(quitting)return;
        int st=Audio.state();bool idle=st==0||st==5;
        if(idle&&!switching&&routeLock!=null){
            ReleaseRoute();
            if(st==0&&SelectionChangedFromRoute&&!scanning)CommitSelection();
        }
        if(st!=lastState){
            StateText.Text=st switch{1=>"正在连接",2=>"正在镜像",3=>"设备重连中",4=>"正在停止",5=>"连接失败",_=>"待播放"};
            Indicator.Fill=new SolidColorBrush(st==2?Color.FromRgb(157,200,155):Color.FromRgb(187,189,185));
            if(st==5)statusMessage="连接失败，请确认两个设备在线，再刷新设备。";
            if(st==2){Plot.Rate=Audio.sample_rate();Plot.InvalidateVisual();}
            lastState=st;
        }
        bool editable=!switching&&!scanning&&!testing&&(idle||st==2||st==3);
        Source.IsEnabled=Target.IsEnabled=Refresh.IsEnabled=editable;
        bool pending=!idle&&SelectionChangedFromRoute;
        ApplyDevices.Visibility=pending&&!switching?Visibility.Visible:Visibility.Collapsed;
        ApplyDevices.IsEnabled=editable&&ValidPair;
        Transport.Content=idle?"开始镜像":"停止镜像";
        bool pair=ValidPair;
        Transport.IsEnabled=!switching&&!scanning&&!testing&&(idle?pair:st!=4);
        TestButton.Content=testing?"正在试听…":"播放测试音";
        TestButton.IsEnabled=!switching&&!testing&&!scanning&&Primary!=null&&(st==0||st==2||st==5);
        GuideListen.IsEnabled=TestButton.IsEnabled&&st==2;
        Delay.IsEnabled=!switching&&pair;TonePagePanel.IsEnabled=!switching&&Secondary!=null;
        Message.Text=switching?"正在切换输出设备…":pending?(pair?"选好设备后，点「应用切换」。":"主音箱与副音箱需要选择不同的设备。"):statusMessage!=""?statusMessage:!pair?"请选择两个不同的输出设备。":st==2?$"WASAPI 共享模式   ·   {Audio.sample_rate()/1000.0:0.#} kHz":"WASAPI 共享模式";
        Message.ToolTip=Message.Text;
        float peak=Audio.take_peak();Level.Width=idle?0:60*Math.Clamp(Math.Max(0,1+20*Math.Log10(Math.Max(peak,.0001f))/60),0,1);
    }
    void SelectPage(bool eq,bool animate){
        Playback.Visibility=eq?Visibility.Collapsed:Visibility.Visible;TonePagePanel.Visibility=eq?Visibility.Visible:Visibility.Collapsed;
        PlaybackTab.SetResourceReference(BackgroundProperty,eq?"Pill":"Surface");ToneTab.SetResourceReference(BackgroundProperty,eq?"Surface":"Pill");
        Scroller.ScrollToTop();
        if(animate&&SystemParameters.ClientAreaAnimation){
            var panel=eq?TonePagePanel:Playback;
            panel.BeginAnimation(OpacityProperty,new DoubleAnimation(.4,1,TimeSpan.FromMilliseconds(180)));
            var shift=new TranslateTransform();panel.RenderTransform=shift;
            shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(7,0,TimeSpan.FromMilliseconds(210)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
        }
    }
    void PlaybackPage(object s,RoutedEventArgs e){if(GuidePanel.IsVisible)ShowGuide(0);else SelectPage(false,true);}
    void TonePage(object s,RoutedEventArgs e){if(GuidePanel.IsVisible)ShowGuide(2);else SelectPage(true,true);}
    void ApplyTheme(){
        dark=config.Theme=="dark"||(config.Theme=="system"&&Equals(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1),0));
        string[] keys={"Paper","Ink","Muted","Line","Surface","Pill"};
        string[] colors=dark?new[]{"#222425","#F5F4EF","#BBBDBA","#464849","#2C2F30","#393C3D"}:new[]{"#F7F6F3","#1B1D20","#5D6266","#D6D5D1","#FFFFFF","#E9E8E4"};
        for(int i=0;i<keys.Length;i++)Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        ThemeButton.Content=config.Theme switch{"dark"=>"外观 · 深色","light"=>"外观 · 浅色",_=>"外观 · 自动"};
    }
    void SystemThemeChanged(object sender,UserPreferenceChangedEventArgs e){if(!quitting)Dispatcher.BeginInvoke(new Action(()=>{if(!quitting && config.Theme=="system")ApplyTheme();}));}
    void CycleTheme(object s,RoutedEventArgs e){config.Theme=config.Theme switch{"system"=>"light","light"=>"dark",_=>"system"};ApplyTheme();ChangeSaved();}
    void OnClosing(object? sender,CancelEventArgs e){
        quitting=true;SystemEvents.UserPreferenceChanged-=SystemThemeChanged;timer.Stop();saveTimer.Stop();if(ready)Save();tray.Visible=false;tray.Dispose();
        if(Audio.shutdown_route()==0)Environment.Exit(1);
        ReleaseRoute();
    }
}
