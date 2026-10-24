using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexIsland;

public static class J
{
    public static JsonElement Get(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var x) ? x : default;
    public static string Str(this JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    public static long Num(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var x) ? x : 0;
    public static double? Real(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var x) && double.IsFinite(x) ? x : null;
}

public sealed class Settings
{
    public string CodexHome { get; set; } = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public string CodexExe { get; set; } = "";
    public int Monitor { get; set; }
    public bool ReducedMotion { get; set; }
    public bool CircleIndicators { get; set; }
    public double CenterRatio { get; set; }=0.5;
    public double TopRatio { get; set; }=0.008;
    public Dictionary<string, Price> Prices { get; set; } = new();
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");
    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
    }
    public void Save()
    {
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
public record Price(double Input, double CachedInput, double Output);
public record Usage(long Input, long Cached, long Output, long Total)
{
    public static Usage Zero = new(0, 0, 0, 0);
    public static Usage Read(JsonElement e) => new(e.Get("input_tokens").Num(), e.Get("cached_input_tokens").Num(), e.Get("output_tokens").Num(), e.Get("total_tokens").Num());
    public static Usage operator +(Usage a, Usage b) => new(a.Input+b.Input,a.Cached+b.Cached,a.Output+b.Output,a.Total+b.Total);
    public static Usage Delta(Usage a, Usage b) => new(Math.Max(0,a.Input-b.Input),Math.Max(0,a.Cached-b.Cached),Math.Max(0,a.Output-b.Output),Math.Max(0,a.Total-b.Total));
}
public record Sample(DateTimeOffset At, Usage Usage, string Model, string Thread);
public record WindowQuota(double Used, long Reset, int Minutes)
{
    public double Remaining => Math.Clamp(100 - Used, 0, 100);
}
public record Quotas(WindowQuota? Week, WindowQuota? Five, string Plan, DateTimeOffset At, string Source)
{
    public static Quotas Parse(JsonElement limits, DateTimeOffset at, string source)
    {
        WindowQuota? week = null, five = null;
        foreach (string key in new[] { "primary", "secondary" })
        {
            var w = limits.Get(key);
            var used = w.Get("usedPercent").Real() ?? w.Get("used_percent").Real();
            long mins = w.Get("windowDurationMins").Num();
            if (mins == 0) mins = w.Get("window_minutes").Num();
            long reset = w.Get("resetsAt").Num();
            if (reset == 0) reset = w.Get("resets_at").Num();
            if (used is null || used < 0 || used > 100 || mins <= 0) continue;
            var quota = new WindowQuota(used.Value, reset, (int)mins);
            if (mins == 10080) week = quota;
            if (mins == 300) five = quota;
        }
        string plan = limits.Get("planType").Str();
        if (plan.Length == 0) plan = limits.Get("plan_type").Str();
        return new(week, five, plan, at, source);
    }
}
public record Snapshot(Usage Today, double Speed, double[] Activity, double? Cost, int Unpriced, string Title, string Model, Quotas? Quotas, int Files, int Errors, DateTimeOffset At)
{
    public double[] DayOutput { get; init; }=new double[24];
    public double AverageOutputPerMinute { get; init; }
}

// Reads only appended bytes after the first scan. No prompts, tool outputs, or credentials are retained.
public sealed class LocalTelemetry
{
    private sealed class Cursor
    {
        public long Offset;
        public string Id = "", Model = "";
        public MemoryStream Pending = new();
        public bool SkipLine;
        public bool IsSubagent;
        public Usage? Previous;
        public bool HasRecords;
        public readonly List<Sample> Legacy = new();
    }
    private readonly Settings settings;
    private readonly Dictionary<string, Cursor> cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sample> records = new();
    private readonly Dictionary<string,string> titles = new();
    private DateTimeOffset discovery;
    private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    private Quotas? quotas;
    private int errors;
    private string latestThread = "", latestModel = "";
    private DateTimeOffset latestAt;
    public LocalTelemetry(Settings settings) { this.settings = settings; }
    public Snapshot Read(DateTimeOffset now, Quotas? remote = null)
    {
        errors = 0;
        var floor = new DateTimeOffset(now.LocalDateTime.Date, now.Offset);
        if ((now - discovery).TotalSeconds >= 30 || discovery == default)
        {
            paths.Clear();
            foreach (string root in new[] { "sessions", "archived_sessions" })
            {
                string dir = Path.Combine(settings.CodexHome, root);
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var p in Directory.EnumerateFiles(dir,"*.jsonl",SearchOption.AllDirectories))
                        if (File.GetLastWriteTimeUtc(p) >= floor.UtcDateTime.AddDays(-1)) paths.Add(p);
                }
                catch (IOException) { errors++; }
                catch (UnauthorizedAccessException) { errors++; }
            }
            ReadTitles();
            discovery = now;
        }
        foreach (string path in paths.ToArray())
        {
            try { ReadFile(path, floor.AddMinutes(-1)); }
            catch (IOException) { errors++; }
            catch (UnauthorizedAccessException) { errors++; }
        }
        // Drop yesterday's records so a resident process stays bounded over time.
        foreach (string key in records.Where(x=>x.Value.At < floor.AddMinutes(-1)).Select(x=>x.Key).ToArray()) records.Remove(key);
        foreach (var c in cursors.Values) c.Legacy.RemoveAll(x=>x.At<floor.AddMinutes(-1));
        var legacy=cursors.Values.Where(c=>!c.HasRecords).SelectMany(c=>c.Legacy)
            .DistinctBy(s => (s.Thread, s.At, s.Usage));
        var samples = records.Values.Concat(legacy).ToList();
        Usage today = Usage.Zero;
        double cost = 0;
        int unpriced = 0;
        var activity = new double[30];
        var dayOutput=new double[24];
        foreach (Sample s in samples)
        {
            if (s.At >= floor && s.At <= now)
            {
                today += s.Usage;
                dayOutput[s.At.ToLocalTime().Hour]+=s.Usage.Output;
                if (PriceCatalog.Resolve(s.Model,settings) is Price p) cost += PriceCatalog.Estimate(s.Usage,p,s.Model);
                else if (s.Usage.Total > 0) unpriced++;
            }
            double age = (now-s.At).TotalSeconds;
            if (age >= 0 && age < 60) activity[29-(int)(age/2)] += s.Usage.Output;
        }
        Quotas? selected = remote;
        if (quotas is not null && (selected is null || quotas.At > selected.At)) selected = quotas;
        string title = titles.GetValueOrDefault(latestThread, "最近活跃会话");
        return new(today, activity.Sum()/60, activity, today.Total==0 || cost>0 || unpriced==0 ? cost : null, unpriced,
            latestThread.Length == 0 ? "等待会话活动" : title, latestModel, selected, cursors.Count, errors, now)
        { DayOutput=dayOutput,AverageOutputPerMinute=today.Output/Math.Max(1,(now-floor).TotalMinutes) };
    }
    public static double Estimate(Usage u, Price p) => (Math.Max(0,u.Input-u.Cached)*p.Input+Math.Min(u.Cached,u.Input)*p.CachedInput+u.Output*p.Output)/1_000_000d;
    private void ReadTitles()
    {
        string path = Path.Combine(settings.CodexHome,"session_index.jsonl");
        if (!File.Exists(path)) return;
        try
        {
            using var f = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var r = new StreamReader(f);
            while (r.ReadLine() is string line)
            {
                try { using var d = JsonDocument.Parse(line); string id=d.RootElement.Get("id").Str(), name=d.RootElement.Get("thread_name").Str(); if(id.Length>0 && name.Length>0) titles[id]=name; }
                catch (JsonException) { }
            }
        }
        catch (IOException) { errors++; }
        catch (UnauthorizedAccessException) { errors++; }
    }
    private void ReadFile(string path, DateTimeOffset floor)
    {
        if (!cursors.TryGetValue(path, out var c)) cursors[path] = c = new();
        using var f = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if (f.Length < c.Offset) { c = new(); cursors[path] = c; }
        if (f.Length == c.Offset) return;
        f.Seek(c.Offset,SeekOrigin.Begin);
        var buffer=new byte[65536]; int count;
        while((count=f.Read(buffer,0,buffer.Length))>0)
        {
            int start=0;
            while(start<count)
            {
                int end=Array.IndexOf(buffer,(byte)'\n',start,count-start);
                int length=(end<0?count:end)-start;
                if(!c.SkipLine)
                {
                    c.Pending.Write(buffer,start,length);
                    if((c.Pending.Length>=220 && !Relevant(c.Pending)) || c.Pending.Length>2*1024*1024)
                    { c.SkipLine=true; c.Pending.SetLength(0); }
                }
                if(end<0) break;
                if(!c.SkipLine && Relevant(c.Pending))
                {
                    try { using var doc=JsonDocument.Parse(c.Pending.GetBuffer().AsMemory(0,(int)c.Pending.Length)); Consume(doc.RootElement,c,floor); }
                    catch(JsonException) { errors++; }
                }
                c.Pending.SetLength(0); if(c.Pending.Capacity>16384) c.Pending.Capacity=1024;
                c.SkipLine=false; start=end+1;
            }
        }
        c.Offset=f.Position;
    }
    private static bool Relevant(MemoryStream line)
    {
        string prefix=Encoding.UTF8.GetString(line.GetBuffer(),0,(int)Math.Min(220,line.Length));
        return prefix.Contains("\"session_meta\"")||prefix.Contains("\"turn_context\"")||prefix.Contains("\"token_usage_record\"")||prefix.Contains("\"token_count\"");
    }
    private void Consume(JsonElement e, Cursor c, DateTimeOffset floor)
    {
        string type=e.Get("type").Str(); var p=e.Get("payload");
        if(type=="session_meta")
        {
            c.Id=p.Get("id").Str();
            c.IsSubagent=p.Get("parent_thread_id").Str().Length>0 || p.Get("source").Get("subagent").ValueKind==JsonValueKind.Object;
            return;
        }
        if(type=="turn_context") { c.Model=p.Get("model").Str(); return; }
        if (!DateTimeOffset.TryParse(e.Get("timestamp").Str(),out var at)) return;
        if(type=="token_usage_record")
        {
            c.HasRecords=true;
            string model=p.Get("model").Str();
            if(model.Length==0) model=c.Model;
            string id=p.Get("response_id").Str();
            string sid=p.Get("thread_id").Str();
            if(sid.Length>0) c.Id=sid;
            if(at>=floor && id.Length>0) records.TryAdd(id,new(at,Usage.Read(p.Get("usage")),model,c.Id));
            Latest(c,at);
        }
        if(type=="event_msg" && p.Get("type").Str()=="token_count")
        {
            var info=p.Get("info").Get("total_token_usage");
            if(info.ValueKind==JsonValueKind.Object)
            {
                var current=Usage.Read(info);
                // The first cumulative counter can contain inherited history; use it only as a baseline.
                if(c.Previous is not null && at>=floor) c.Legacy.Add(new(at,Usage.Delta(current,c.Previous),c.Model,c.Id));
                c.Previous=current;
                Latest(c,at);
            }
            var limit=p.Get("rate_limits");
            if(limit.ValueKind==JsonValueKind.Object && (quotas is null || at>quotas.At) &&
                (limit.Get("limit_id").Str() is "codex" or "")) quotas=Quotas.Parse(limit,at,"本地日志");
        }
    }
    private void Latest(Cursor c,DateTimeOffset at) { if(!c.IsSubagent && at>=latestAt) { latestAt=at; latestThread=c.Id; latestModel=c.Model; } }
}

public static class AccountProbe
{
    public static string? FindCodex(Settings settings)
    {
        if(File.Exists(settings.CodexExe)) return settings.CodexExe;
        foreach(string path in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        { string p=Path.Combine(path,"codex.exe"); if(File.Exists(p)) return p; }
        string local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(local)) return Directory.EnumerateFiles(local,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return null;
    }
    public static async Task<Quotas> Read(Settings settings,CancellationToken stop)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        string exe=FindCodex(settings)??throw new InvalidOperationException("未找到 Codex CLI，可在设置中指定路径");
        using var proc=new Process { StartInfo=new ProcessStartInfo(exe,"app-server") { RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,UseShellExecute=false } };
        proc.StartInfo.Environment["CODEX_HOME"]=settings.CodexHome;
        proc.Start();
        using var kill=timeout.Token.Register(()=> { try { if(!proc.HasExited) proc.Kill(true); } catch(InvalidOperationException){} });
        _ = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id=1,method="initialize",@params=new { clientInfo=new { name="codex_island",version="0.1.0" } } }));
            await Receive(proc,1,timeout.Token);
            await proc.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await proc.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
            var result=await Receive(proc,2,timeout.Token);
            var bucket=result.Get("rateLimitsByLimitId").Get("codex");
            if(bucket.ValueKind!=JsonValueKind.Object) bucket=result.Get("rateLimits");
            if(bucket.ValueKind!=JsonValueKind.Object) throw new InvalidOperationException("账户未返回额度");
            return Quotas.Parse(bucket,DateTimeOffset.Now,"账户实时");
        }
        finally { try { if(!proc.HasExited) proc.Kill(true); } catch(InvalidOperationException){} await proc.WaitForExitAsync(); }
    }
    private static async Task<JsonElement> Receive(Process p,int id,CancellationToken token)
    {
        while(true)
        {
            string? line=await p.StandardOutput.ReadLineAsync().WaitAsync(token);
            if(line is null) throw new IOException("Codex 额度连接已结束");
            try
            {
                using var doc=JsonDocument.Parse(line); var root=doc.RootElement;
                if(root.Get("id").Num()!=id) continue;
                if(root.Get("error").ValueKind==JsonValueKind.Object) throw new InvalidOperationException("额度读取失败，请确认 Codex 已登录");
                return root.Get("result").Clone();
            }
            catch(JsonException) { }
        }
    }
}
