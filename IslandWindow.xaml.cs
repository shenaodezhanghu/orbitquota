using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace CodexIsland;
public partial class IslandWindow : Window
{
    private readonly Settings settings;
    private readonly LocalTelemetry telemetry;
    private readonly bool demo;
    private readonly CancellationTokenSource stop=new();
    private readonly DispatcherTimer poll=new() { Interval=TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer leave=new() { Interval=TimeSpan.FromMilliseconds(480) };
    private readonly DispatcherTimer enter=new() { Interval=TimeSpan.FromMilliseconds(130) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private bool expanded,pinned,reading,probing,closing;
    private DateTimeOffset nextProbe;
    private Quotas? remote;
    private string probeError="";
    private Snapshot? latest;
    private int selection;
    private int view;
    private bool usageSelected;
    private bool moving,dragged;
    private System.Drawing.Point moveStart;
    private NativeRect initialRect;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    public IslandWindow(bool demo)
    {
        this.demo=demo;
        try { settings=Settings.Load(); }
        catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException) { settings=new(); MessageBox.Show("配置读取失败，使用默认设置："+ex.Message,"Codex Island"); }
        telemetry=new(settings); selection=settings.Monitor;
        InitializeComponent(); ShowActivated=false; Shell.ClipToBounds=true;
        Shell.Background=Brushes.Transparent; Shell.BorderBrush=Brushes.Transparent;
        Details.Width=288; Details.HorizontalAlignment=HorizontalAlignment.Left;
        SelectPage(false,false);
        ToolTipService.SetIsEnabled(Shell,false);
        Shell.ContextMenu=BuildMenu();
        SourceInitialized+=(_,_)=>PositionAtTop();
        SizeChanged+=(_,_)=>PositionAtTop();
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(new Action(PositionAtTop));
        KeyDown+=(_,e)=> { if(e.Key==Key.Escape) { pinned=false; SetExpanded(false); } };
        enter.Tick+=(_,_)=> { enter.Stop(); if(Shell.IsMouseOver&&!moving&&!pinned) SetView(1); };
        leave.Tick+=(_,_)=> { leave.Stop(); if(!moving&&!Shell.IsMouseOver&&!pinned&&!(Shell.ContextMenu?.IsOpen??false)) SetExpanded(false); };
        Loaded+=async (_,_)=> { MakeTray(); await Refresh(); poll.Start(); };
        poll.Tick+=async (_,_)=>await Refresh();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=OnDisplayChange;
        Closed+=(_,_)=> { closing=true; poll.Stop(); enter.Stop(); leave.Stop(); stop.Cancel(); tray?.Dispose(); trayIcon?.Dispose(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=OnDisplayChange; };
    }
    private void OnDisplayChange(object? sender,EventArgs e) => Dispatcher.BeginInvoke(new Action(PositionAtTop));
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private void PositionAtTop()
    {
        if(moving) return;
        var handle=new WindowInteropHelper(this).Handle; if(handle==IntPtr.Zero) return;
        var screens=Forms.Screen.AllScreens; var screen=screens[Math.Clamp(selection,0,screens.Length-1)];
        double scale=VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width=(int)Math.Round(ActualWidth*scale),height=(int)Math.Round(ActualHeight*scale);
        int x=screen.WorkingArea.Left+(int)(screen.WorkingArea.Width*Math.Clamp(settings.CenterRatio,0,1))-width/2;
        int y=screen.WorkingArea.Top+(int)(screen.WorkingArea.Height*Math.Clamp(settings.TopRatio,0,1));
        x=Math.Clamp(x,screen.WorkingArea.Left,Math.Max(screen.WorkingArea.Left,screen.WorkingArea.Right-width));
        y=Math.Clamp(y,screen.WorkingArea.Top,Math.Max(screen.WorkingArea.Top,screen.WorkingArea.Bottom-height));
        SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x0015); // no size, z-order, or activation change
    }
    private void OnEnter(object sender,MouseEventArgs e) { leave.Stop(); if(!expanded) enter.Start(); }
    private void OnLeave(object sender,MouseEventArgs e) { enter.Stop(); leave.Start(); }
    private static bool IsButton(object? source)
    {
        var visual=source as DependencyObject;
        while(visual is not null) { if(visual is Button) return true; visual=VisualTreeHelper.GetParent(visual); }
        return false;
    }
    private void BeginMove(object sender,MouseButtonEventArgs e)
    {
        if(IsButton(e.OriginalSource)) return;
        moving=true; dragged=false; moveStart=Forms.Cursor.Position;
        GetWindowRect(new WindowInteropHelper(this).Handle,out initialRect);
        Shell.CaptureMouse();
    }
    private void MoveIsland(object sender,MouseEventArgs e)
    {
        if(!moving||e.LeftButton!=MouseButtonState.Pressed) return;
        var point=Forms.Cursor.Position; int dx=point.X-moveStart.X,dy=point.Y-moveStart.Y;
        if(!dragged && Math.Abs(dx)+Math.Abs(dy)<5) return;
        dragged=true; enter.Stop(); leave.Stop();
        int x=initialRect.Left+dx,y=initialRect.Top+dy;
        SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,x,y,0,0,0x0015);
        var screen=Forms.Screen.FromPoint(point); selection=Array.IndexOf(Forms.Screen.AllScreens,screen);
        if(selection<0) selection=Array.FindIndex(Forms.Screen.AllScreens,s=>s.DeviceName==screen.DeviceName);
        settings.Monitor=Math.Max(0,selection);
        settings.CenterRatio=Math.Clamp((x+ActualWidth*VisualTreeHelper.GetDpi(this).DpiScaleX/2-screen.WorkingArea.Left)/screen.WorkingArea.Width,0,1);
        settings.TopRatio=Math.Clamp((y-screen.WorkingArea.Top)/(double)screen.WorkingArea.Height,0,1);
    }
    private void EndMove(object sender,MouseButtonEventArgs e)
    {
        if(!moving) return;
        moving=false; Shell.ReleaseMouseCapture();
        if(dragged) { e.Handled=true; PositionAtTop(); SaveSettings(); }
    }
    private void ShowStats()
    {
        var info=new SettingsWindow(settings,latest) { Owner=this }; info.ShowDialog();
    }
    private void OnShellClick(object sender,MouseButtonEventArgs e)
    {
        if(e.Handled||dragged) return;
        DependencyObject? current=e.OriginalSource as DependencyObject;
        while(current is not null && current!=Shell) { if(current is Button) return; current=VisualTreeHelper.GetParent(current); }
        pinned=!pinned; SetView(pinned?2:Shell.IsMouseOver?1:0);
    }
    private void SelectQuota(object sender,RoutedEventArgs e) { e.Handled=true; SelectPage(false,true); }
    private void SelectUsage(object sender,RoutedEventArgs e) { e.Handled=true; SelectPage(true,true); }
    private void SelectPage(bool usage,bool animate)
    {
        usageSelected=usage;
        QuotaPage.Visibility=usage?Visibility.Collapsed:Visibility.Visible;
        UsagePage.Visibility=usage?Visibility.Visible:Visibility.Collapsed;
        QuotaTab.Background=new SolidColorBrush(usage?Colors.Transparent:Color.FromRgb(48,55,67));
        UsageTab.Background=new SolidColorBrush(usage?Color.FromRgb(48,55,67):Colors.Transparent);
        QuotaTab.Foreground=new SolidColorBrush(usage?Color.FromRgb(132,141,155):Colors.White);
        UsageTab.Foreground=new SolidColorBrush(usage?Colors.White:Color.FromRgb(132,141,155));
        if(animate&&!settings.ReducedMotion)
        {
            var page=usage?UsagePage:QuotaPage;
            page.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(170)));
        }
        if(usage&&latest is not null) DrawActivity(latest.DayOutput);
    }
    private void SetExpanded(bool value)=>SetView(value?2:0);
    private void SetView(int state)
    {
        if(view==state) return;
        int previous=view; view=state; expanded=state==2;
        double millis=settings.ReducedMotion||!SystemParameters.ClientAreaAnimation?1:state>previous?370:290;
        Details.Visibility=state==2?Visibility.Visible:Visibility.Hidden;
        Details.IsHitTestVisible=state==2;
        Shell.Background=state==2?new LinearGradientBrush(Color.FromRgb(16,28,45),Color.FromRgb(12,18,28),90):Brushes.Transparent;
        Shell.BorderBrush=state==2?new SolidColorBrush(Color.FromRgb(52,65,86)):Brushes.Transparent;
        var ease=new CubicEase { EasingMode=EasingMode.EaseInOut };
        BeginAnimation(WidthProperty,new DoubleAnimation(state==2?340:state==1?194:178,TimeSpan.FromMilliseconds(millis)) { EasingFunction=ease });
        BeginAnimation(HeightProperty,new DoubleAnimation(state==2?398:178,TimeSpan.FromMilliseconds(millis)) { EasingFunction=ease });
        if(state==2) Details.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(millis*0.6)) { BeginTime=TimeSpan.FromMilliseconds(millis*0.25) });
        if(latest is not null) UpdateSky(latest);
    }
    private void UpdateSky(Snapshot s)
    {
        bool old=s.Quotas is not null&&(DateTimeOffset.Now-s.Quotas.At).TotalMinutes>5;
        Sky.Floating=view!=2;
        Sky.Update(s.Quotas,s.Speed*60,old,settings.ReducedMotion||!SystemParameters.ClientAreaAnimation,view>0);
    }
    private async Task Refresh()
    {
        if(reading||closing) return; reading=true;
        try
        {
            if(demo)
            {
                latest=new(new(1264000,1100000,28600,1292600),42.6,Enumerable.Range(0,30).Select(i=>1200+Math.Sin(i*0.7)*800).ToArray(),0.82,0,"界面演示 · 虚构数据","DEMO",new(new(32,DateTimeOffset.Now.AddDays(3).ToUnixTimeSeconds(),10080),new(18,DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds(),300),"DEMO",DateTimeOffset.Now,"演示"),0,0,DateTimeOffset.Now) { AverageOutputPerMinute=320,DayOutput=new double[] { 0,0,0,0,0,0,0,0,320,1100,830,2000,620,1240,2100,1700,900,0,0,0,0,0,0,0 } };
            }
            else
            {
                latest=await Task.Run(()=>telemetry.Read(DateTimeOffset.Now,remote),stop.Token);
                if(DateTimeOffset.Now>=nextProbe&&!probing) _=RefreshQuota();
            }
            if(!closing) Render(latest);
        }
        catch(OperationCanceledException) { }
        catch(Exception) { if(!closing) { Status.Text="读取失败"; LiveDot.Fill=new SolidColorBrush(Colors.Orange); } }
        finally { reading=false; }
    }
    private async Task RefreshQuota()
    {
        probing=true; nextProbe=DateTimeOffset.Now.AddSeconds(120);
        try { remote=await AccountProbe.Read(settings,stop.Token); probeError=""; }
        catch(OperationCanceledException) { probeError="额度读取超时"; }
        catch(Exception ex) { probeError=ex.Message; }
        finally { probing=false; if(!closing && latest is not null) { latest=latest with { Quotas=remote??latest.Quotas }; Render(latest); } }
    }
    private static string CompactNumber(long n) => n>=1_000_000?(n/1_000_000d).ToString("0.00")+"M":n>=1000?(n/1000d).ToString("0.0")+"K":n.ToString("N0");
    private static string ResetLabel(WindowQuota? q)
    {
        if(q is null) return "账户未提供";
        if(q.Reset<=0) return "重置时间未知";
        try
        {
            var date=DateTimeOffset.FromUnixTimeSeconds(q.Reset).ToLocalTime();
            var remaining=date-DateTimeOffset.Now;
            return remaining.TotalSeconds<0?"等待刷新":remaining.TotalDays>=1?$"{(int)remaining.TotalDays}天 {remaining.Hours}小时后重置":remaining.TotalHours>=1?$"{(int)remaining.TotalHours}小时 {remaining.Minutes}分钟后重置":$"{Math.Max(1,remaining.Minutes)}分钟后重置";
        }
        catch(ArgumentOutOfRangeException) { return "重置时间未知"; }
    }
    private void Render(Snapshot s)
    {
        var q=s.Quotas;
        bool stale=q is not null && (DateTimeOffset.Now-q.At).TotalMinutes>5;
        UpdateSky(s);
        WeekValue.Text=q?.Week is null?"—":q.Week.Remaining.ToString("0")+"%";
        FiveValue.Text=q?.Five is null?"—":q.Five.Remaining.ToString("0")+"%";
        WeekFill.Width=288*(q?.Week?.Remaining??0)/100;
        FiveFill.Width=288*(q?.Five?.Remaining??0)/100;
        WeekReset.Text=ResetLabel(q?.Week); FiveReset.Text=ResetLabel(q?.Five);
        TodayTokens.Text=CompactNumber(s.Today.Total);
        Speed.Text=s.AverageOutputPerMinute.ToString("N0")+" tokens";
        DayLabel.Text=demo?"示例数据":s.At.ToLocalTime().ToString("MM/dd")+" · 今日 Tokens";
        Cost.Text=s.Cost.HasValue?(s.Unpriced>0?"≥ $":"≈ $")+s.Cost.Value.ToString("F2"):"—";
        CostCaption.Text=s.Unpriced>0?"API 等价 · 部分":"API 等价 · USD";
        Model.Text=s.Model.Length>0?s.Model:"等待会话";
        Status.Text=demo?"演示":q is null?"未连接":stale?"旧快照":probeError.Length>0?"离线":"已同步";
        LiveDot.Fill=new SolidColorBrush(stale||q is null||probeError.Length>0?Color.FromRgb(210,176,116):Color.FromRgb(163,211,188));
        DrawActivity(s.DayOutput);
        if(tray is not null) tray.Text=$"OrbitQuota · 周 {WeekValue.Text} / 5h {FiveValue.Text}";
    }
    private ContextMenu BuildMenu()
    {
        var menu=new ContextMenu { Background=new SolidColorBrush(Color.FromRgb(23,24,28)) };
        void Add(string label,Action run) { var item=new MenuItem { Header=label }; item.Click+=(_,_)=>run(); menu.Items.Add(item); }
        Add("显示 / 固定详情",()=> { Show(); pinned=true; SetExpanded(true); });
        Add("收起",()=> { pinned=false; SetExpanded(false); });
        Add("刷新",()=> { nextProbe=default; _=Refresh(); });
        Add("恢复顶部居中",()=> { settings.CenterRatio=0.5; settings.TopRatio=0.008; PositionAtTop(); SaveSettings(); });
        Add("统计说明",()=>ShowStats());
        Add("设置",()=>OpenSettings(this,new RoutedEventArgs()));
        var screens=new MenuItem { Header="显示器" };
        for(int i=0;i<Forms.Screen.AllScreens.Length;i++)
        {
            int index=i; var item=new MenuItem { Header=$"显示器 {i+1}" };
            item.Click+=(_,_)=> { selection=index; settings.Monitor=index; PositionAtTop(); SaveSettings(); }; screens.Items.Add(item);
        }
        menu.Items.Add(screens);
        Add("隐藏到托盘",()=>Hide());
        Add("退出",()=>Close()); return menu;
    }
    private void DrawActivity(double[] values)
    {
        if(view!=2||!usageSelected) return;
        Spark.Children.Clear();
        double max=Math.Max(1,values.Max());
        for(int i=0;i<24;i++)
        {
            double value=i<values.Length?values[i]:0;
            double height=Math.Max(1.1,value/max*25);
            var bar=new System.Windows.Shapes.Rectangle { Width=5,Height=height,RadiusX=1.3,RadiusY=1.3,Fill=new SolidColorBrush(value>0?Color.FromRgb(132,161,199):Color.FromRgb(39,54,75)) };
            Canvas.SetLeft(bar,i*11.5+4); Canvas.SetTop(bar,28-height); Spark.Children.Add(bar);
        }
        for(int i=0;i<=4;i++)
        {
            var label=new TextBlock { Text=(i*6).ToString("00"),FontSize=7,Foreground=new SolidColorBrush(Color.FromRgb(88,110,142)) };
            Canvas.SetLeft(label,i*67.5+1); Canvas.SetTop(label,32); Spark.Children.Add(label);
        }
    }
    private void MakeTray()
    {
        using var bitmap=new System.Drawing.Bitmap(32,32);
        using(var g=System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var a=new System.Drawing.Pen(System.Drawing.Color.FromArgb(119,232,187),3);
            using var b=new System.Drawing.Pen(System.Drawing.Color.FromArgb(166,177,255),3);
            g.DrawEllipse(a,2,9,12,12); g.DrawEllipse(b,18,9,12,12);
        }
        IntPtr raw=bitmap.GetHicon(); using(var borrowed=System.Drawing.Icon.FromHandle(raw)) trayIcon=(System.Drawing.Icon)borrowed.Clone(); DestroyIcon(raw);
        tray=new Forms.NotifyIcon { Icon=trayIcon,Text="OrbitQuota",Visible=true };
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("显示详情",null,(_,_)=>Dispatcher.Invoke(()=> { Show(); pinned=true; SetExpanded(true); PositionAtTop(); }));
        menu.Items.Add("设置",null,(_,_)=>Dispatcher.Invoke(()=>OpenSettings(this,new RoutedEventArgs())));
        menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Close)); tray.ContextMenuStrip=menu;
        tray.MouseClick+=(_,e)=> { if(e.Button==Forms.MouseButtons.Left) Dispatcher.Invoke(()=> { if(!IsVisible) Show(); pinned=true; SetExpanded(true); PositionAtTop(); }); };
    }
    private void HideIsland(object sender,RoutedEventArgs e) { e.Handled=true; Hide(); }
    private void RefreshClick(object sender,RoutedEventArgs e) { e.Handled=true; nextProbe=default; _=Refresh(); }
    private void SaveSettings() { try { settings.Save(); } catch(IOException ex) { MessageBox.Show("配置保存失败："+ex.Message); } catch(UnauthorizedAccessException ex) { MessageBox.Show("配置保存失败："+ex.Message); } }
    private void OpenSettings(object sender,RoutedEventArgs e)
    {
        e.Handled=true; leave.Stop();
        var dialog=new SettingsWindow(settings,latest) { Owner=this };
        dialog.ShowDialog(); nextProbe=default; _=Refresh(); if(!pinned&&!Shell.IsMouseOver) SetExpanded(false);
    }
    internal async Task Capture()
    {
        Directory.CreateDirectory("diagnostics");
        await Task.Delay(1000);
        for(int i=0;i<60 && (reading||probing);i++) await Task.Delay(500);
        SetView(1); await Task.Delay(600); SaveImage(this,"diagnostics/peek.png");
        if(Details.IsHitTestVisible) throw new Exception("Peek should not intercept detail clicks");
        pinned=true; SetExpanded(true); await Task.Delay(850);
        SaveImage(this,"diagnostics/expanded.png");
        if(!expanded || ActualWidth<330 || Details.Opacity<0.95) throw new Exception("Expand animation did not finish");
        UsageTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
        if(UsagePage.Visibility!=Visibility.Visible||QuotaPage.Visibility!=Visibility.Collapsed) throw new Exception("Usage tab did not switch");
        SaveImage(this,"diagnostics/usage.png");
        QuotaTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
        if(QuotaPage.Visibility!=Visibility.Visible||UsagePage.Visibility!=Visibility.Collapsed) throw new Exception("Quota tab did not switch back");
        pinned=false; SetExpanded(false); await Task.Delay(650);
        SaveImage(this,"diagnostics/compact.png");
        if(ActualWidth>184) throw new Exception("Collapse animation did not finish");
        if(ToolTipService.GetIsEnabled(this)||ToolTipService.GetIsEnabled(Shell)) throw new Exception("Hover tooltips should be disabled");
        var handle=new WindowInteropHelper(this).Handle;
        GetWindowRect(handle,out var before);
        double previousX=settings.CenterRatio,previousY=settings.TopRatio;
        settings.CenterRatio=previousX<0.6?0.75:0.25; settings.TopRatio=0.25;
        PositionAtTop(); await Task.Delay(100); GetWindowRect(handle,out var moved);
        if(before.Left==moved.Left && before.Top==moved.Top) throw new Exception("Position should be movable");
        settings.CenterRatio=previousX; settings.TopRatio=previousY; PositionAtTop();
        var dialog=new SettingsWindow(settings,latest) { Owner=this }; dialog.Show();
        await Task.Delay(300); SaveImage(dialog,"diagnostics/settings.png"); dialog.Close();
        var s=latest;
        File.WriteAllText("diagnostics/live-check.json",System.Text.Json.JsonSerializer.Serialize(new { demo,todayTokens=s?.Today.Total,equivalentUsd=s?.Cost,unpriced=s?.Unpriced,outputPerMinute=s?.AverageOutputPerMinute,files=s?.Files,errors=s?.Errors,quotaSource=s?.Quotas?.Source,weeklyRemaining=s?.Quotas?.Week?.Remaining,fiveHourRemaining=s?.Quotas?.Five?.Remaining,solarProgress=OrbitMath.Progress(s?.Quotas?.Week,DateTimeOffset.Now),lunarProgress=OrbitMath.Progress(s?.Quotas?.Five,DateTimeOffset.Now),probeError,runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,privateMemoryMB=Process.GetCurrentProcess().PrivateMemorySize64/1048576,windowTests="orbit scene, peek, quota tab, fixed daily usage tab, compact, position movement, disabled hover tooltips" },new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
    }
    private static void SaveImage(Window window,string path)
    {
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth*2,(int)window.ActualHeight*2,192,192,PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path); encoder.Save(stream);
    }
}
