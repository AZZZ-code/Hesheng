using System.Windows;

namespace Hesheng;

public partial class MainWindow {
    private int guideStep;
    private static readonly (string Heading, string Body, string Hint)[] GuideSteps = {
        ("选择两台播放设备",
         "主音箱选正在播放音乐的设备，通常是标有「系统默认」的那一项。展开「副音箱」，选择另一台你想同时听到的音箱或耳机。",
         ""),
        ("确认两边都能听到",
         "点「开始镜像」，等状态变成「正在镜像」，再点「播放测试音」。主、副音箱应该各响三声。也可以播放一小段熟悉的音乐来确认。",
         "只有主音箱响？检查副音箱的音量、静音和连接。测试音能响但音乐没有同步时，检查音乐应用是否输出到了所选主音箱。"),
        ("用均衡器调整副音箱",
         "播放熟悉的歌，从「原声」开始。想让对白清楚一些，可以先选「人声」，或把 1 kHz 推高 2 dB。80 Hz 和 250 Hz 调低音的力度与厚度，4 kHz 和 10 kHz 调清晰度与明亮感。",
         "每次只调一段。提高频段后如果声音发硬，可把「前级增益」调低。点「效果已开启」可旁通试听，再点一次恢复；满意后输入名称，点「保存预设」。"),
        ("把两下鼓点听成一下",
         "在「播放」页从 0 ms 开始，播放鼓点清楚的音乐或反复点「播放测试音」。如果副音箱先响，每次增加 5 ms，直到两边听起来像同一下。若回声变明显，就往回减。",
         "补偿只会推迟副音箱。副音箱在 0 ms 时已经较晚，就保留 0 ms。合声会为这对设备记住延迟，之后可以在右上角重看教程。")
    };

    void OpenGuide(object sender,RoutedEventArgs e)=>ShowGuide(0);
    void BackGuide(object sender,RoutedEventArgs e)=>ShowGuide(guideStep-1);
    void NextGuide(object sender,RoutedEventArgs e){
        if(guideStep+1<GuideSteps.Length)ShowGuide(guideStep+1);
        else CloseGuide();
    }
    void DismissGuide(object sender,RoutedEventArgs e)=>CloseGuide();
    void CloseGuide(){
        GuidePanel.Visibility=Visibility.Collapsed;
        PlaybackHero.Visibility=Visibility.Visible;
        GuideButton.Focus();
    }
    void ShowGuide(int step){
        guideStep=step;
        GuidePanel.Visibility=Visibility.Visible;
        GuideProgress.Text=$"使用教程   ·   {step+1} / {GuideSteps.Length}";
        GuideHeading.Text=GuideSteps[step].Heading;
        GuideBody.Text=GuideSteps[step].Body;
        GuideHint.Text=GuideSteps[step].Hint;
        UpdateGuideHint();
        GuideBack.Visibility=step==0?Visibility.Collapsed:Visibility.Visible;
        GuideNext.Content=step==GuideSteps.Length-1?"完成教程":"下一步";
        GuideListen.Visibility=step==3?Visibility.Visible:Visibility.Collapsed;
        PlaybackHero.Visibility=step==0||step==3?Visibility.Collapsed:Visibility.Visible;
        SelectPage(step==2,true);
        if(!config.HasSeenGuide){
            config.HasSeenGuide=true;
            Save();
        }
    }
    void UpdateGuideHint(){
        if(GuidePanel.Visibility!=Visibility.Visible)return;
        if(guideStep==0){
            int count=Source.Items.Count;
            GuideHint.Text=count switch{
                0=>"当前没有可用输出。先在 Windows 声音设置中连接并启用设备，然后点「刷新设备」。",
                1=>"目前只有一个可用输出。连接第二台设备后，点「刷新设备」再选择。",
                _=>$"已识别此电脑的 {count} 个播放端点，列表也可能包含显示器或虚拟设备。蓝牙或 USB 设备刚接入时，可点「刷新设备」。"
            };
        }else if(guideStep==2&&Secondary==null){
            GuideHint.Text="先回到「播放」页选好副音箱，调音控件就能使用。";
        }
    }
}
