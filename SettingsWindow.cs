using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexIsland;
public sealed class SettingsWindow : Window
{
    public SettingsWindow(Settings settings,Snapshot? snapshot)
    {
        Title="OrbitQuota"; Width=390; Height=535; ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(18,19,23));
        ToolTipService.SetIsEnabled(this,false);
        var panel=new StackPanel { Margin=new Thickness(25) }; Content=panel;
        void Text(string text,int top=14,int size=12) => panel.Children.Add(new TextBlock { Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,top,0,0),Foreground=new SolidColorBrush(Color.FromRgb(156,158,167)) });
        panel.Children.Add(new TextBlock { Text="统计与偏好",FontSize=20,FontWeight=FontWeights.Medium });
        Text("订阅额度是真实剩余额度；美元是同等 Token 按标准 API 单价折算的参考值，不是额外扣费。",12);
        Text("太阳外圈一周，月亮内圈五小时，均顺时针。位置表示刷新进度，光芒/月相表示剩余额度。地球的二维昼夜分区朝向太阳，不模拟实际天文月相。",12,11);
        if(snapshot is not null)
        {
            Text($"今日输入  {snapshot.Today.Input:N0}  ·  缓存 {snapshot.Today.Cached:N0}\n今日输出  {snapshot.Today.Output:N0}",16);
            Text(snapshot.Unpriced>0?$"{snapshot.Unpriced} 条记录没有公开价格，仅汇总可折算部分（≥）。包含内部模型时不猜测价格。":"模型已自动匹配，费用按各条请求分别计算。",10);
            Text($"周额度：{Reset(snapshot.Quotas?.Week)}\n五小时：{Reset(snapshot.Quotas?.Five)}",12);
        }
        Text($"价格核验：{PriceCatalog.Verified} · 标准 API 档\n未包含工具费用；TTFT 当前无可靠数据，主界面不展示。",12,11);
        var motion=new CheckBox { Content="减少动效",IsChecked=settings.ReducedMotion,Foreground=Brushes.White,Margin=new Thickness(0,18,0,0) }; panel.Children.Add(motion);
        Text("按住小景拖动，松开后记住位置。点击打开分页，再点空白返回小景。右键可恢复顶部居中。位置每秒持续计算；日均输出按今日已过分钟计算。",12,11);
        var row=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0) };
        var source=new Button { Content="价格来源" }; source.Click+=(_,_)=>Process.Start(new ProcessStartInfo(PriceCatalog.Source) { UseShellExecute=true }); row.Children.Add(source);
        var save=new Button { Content="完成",Margin=new Thickness(8,0,0,0) };
        save.Click+=(_,_)=> { bool old=settings.ReducedMotion; settings.ReducedMotion=motion.IsChecked==true;
            try { settings.Save(); Close(); }
            catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException) { settings.ReducedMotion=old; MessageBox.Show("保存失败："+ex.Message); }
        };
        row.Children.Add(save); panel.Children.Add(row);
    }
    private static string Reset(WindowQuota? window)
    {
        if(window is null||window.Reset<=0) return "未提供";
        try { return DateTimeOffset.FromUnixTimeSeconds(window.Reset).ToLocalTime().ToString("MM/dd HH:mm")+" 重置"; }
        catch(ArgumentOutOfRangeException) { return "时间未知"; }
    }
}
