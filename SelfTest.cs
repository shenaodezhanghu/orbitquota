using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodexIsland;
public static class SelfTest
{
    private static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static void Run()
    {
        string temp=Path.Combine(Path.GetTempPath(),"codex-island-test-"+Guid.NewGuid()); Directory.CreateDirectory(Path.Combine(temp,"sessions"));
        try
        {
            using var doc=JsonDocument.Parse("{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":10080},\"secondary\":{\"usedPercent\":5,\"windowDurationMins\":300}}");
            var q=Quotas.Parse(doc.RootElement,DateTimeOffset.Now,"test");
            Assert(q.Week?.Remaining==75&&q.Five?.Remaining==95,"Quota windows must be classified by duration, not primary/secondary");
            using var missing=JsonDocument.Parse("{\"primary\":null}");
            Assert(Quotas.Parse(missing.RootElement,DateTimeOffset.Now,"test").Week is null,"Missing must not become zero");
            var clock=DateTimeOffset.Parse("2026-10-22T12:00:00Z");
            var half=new WindowQuota(70,clock.AddMinutes(150).ToUnixTimeSeconds(),300);
            Assert(Math.Abs(OrbitMath.Progress(half,clock)!.Value-.5)<1e-9,"Moon position must derive from reset timestamp and five-hour duration");
            Assert(Math.Abs(OrbitMath.Progress(half,clock.AddSeconds(1))!.Value-.5-1d/18000)<1e-8,"Moon must move by real elapsed time between data polls");
            Assert(OrbitMath.Progress(half,clock.AddHours(3))==1,"Expired quota must stop at endpoint, not loop into a fabricated new window");
            Assert(OrbitMath.Progress(half,clock.AddHours(-4))==0,"Future window start should clamp to left endpoint");
            Assert(OrbitMath.Progress(null,clock) is null,"Missing quota cannot invent a celestial position");
            Assert(OrbitMath.Progress(half with { Used=0 },clock)==OrbitMath.Progress(half,clock),"Consumed quota cannot change time position");
            var week=new WindowQuota(70,clock.AddDays(3.5).ToUnixTimeSeconds(),10080);
            Assert(Math.Abs(OrbitMath.Progress(week,clock)!.Value-.5)<1e-9,"Solar movement uses an independent seven-day window");
            Assert((OrbitMath.Moon(0)-OrbitMath.Moon(1)).Length<1e-8 && (OrbitMath.Sun(0)-OrbitMath.Sun(1)).Length<1e-8,"Both paths must be closed ellipses");
            foreach(var orbit in new Func<double,System.Windows.Point>[] { OrbitMath.Sun,OrbitMath.Moon })
            {
                var a=orbit(0)-OrbitMath.Earth;var b=orbit(.001)-OrbitMath.Earth;
                Assert(a.X*b.Y-a.Y*b.X>0,"Both orbits must travel clockwise in screen coordinates");
            }
            Assert(OrbitMath.SunLight(.25).Z==0&&OrbitMath.SunLight(.75).Z==0,"Flat scene must have no depth component");
            Assert(Math.Abs((OrbitMath.Sun(.2)-OrbitMath.Earth).Length-48)<1e-8 && Math.Abs((OrbitMath.Moon(.8)-OrbitMath.Earth).Length-31)<1e-8,"Both flat tracks must be concentric circles");
            Assert(Math.Abs(OrbitMath.SunLight(.4).Length-1)<1e-8,"Earth light direction must be normalized");
            var globe=new EarthSurface();
            var day=(System.Windows.Media.Imaging.BitmapSource)globe.Get(OrbitMath.SunLight(0));
            byte[] dayPixels=new byte[112*112*4];day.CopyPixels(dayPixels,112*4,0);
            var night=(System.Windows.Media.Imaging.BitmapSource)globe.Get(OrbitMath.SunLight(.5));
            byte[] nightPixels=new byte[dayPixels.Length];night.CopyPixels(nightPixels,112*4,0);
            int centerPixel=(28*112+56)*4;
            Assert(dayPixels[centerPixel]>nightPixels[centerPixel]+30,"Earth's sun-facing flat half must be brighter than its night half");
            Assert(Math.Abs(LocalTelemetry.Estimate(new(1000,800,100,1100),new(2,0.2,10))-0.00156)<1e-9,"Cost must avoid counting cached input twice");
            var automatic=new Settings();
            Assert(PriceCatalog.Resolve("gpt-6.1-sol",automatic)==new Price(2,0.1,10),"Automatic public model pricing");
            Assert(PriceCatalog.Resolve("codex-auto-review",automatic) is null,"Unknown internal models must not get guessed prices");
            Assert(PriceCatalog.Resolve("gpt-6.1-sol-2026-09-29",automatic)==new Price(2,0.1,10),"Dated model normalization");
            Assert(PriceCatalog.Estimate(new(300000,0,1000,301000),new(2,0.1,10),"gpt-6.1-sol")>1.2,"Long context price adjustment");
            var settings=new Settings { CodexHome=temp }; settings.Prices["test-model"]=new(2,0.2,10);
            var scanner=new LocalTelemetry(settings); var now=DateTimeOffset.Now;
            string path=Path.Combine(temp,"sessions","test.jsonl");
            string Record(string id,string time="")=>JsonSerializer.Serialize(new { timestamp=time.Length>0?time:now.ToString("O"),type="token_usage_record",payload=new { response_id=id,thread_id="t1",usage=new { input_tokens=1000,cached_input_tokens=800,output_tokens=100,total_tokens=1100 } } });
            File.WriteAllText(path,"{\"type\":\"session_meta\",\"payload\":{\"id\":\"t1\"}}\n{\"type\":\"turn_context\",\"payload\":{\"model\":\"test-model\"}}\n"+Record("r1")+"\n");
            File.WriteAllText(Path.Combine(temp,"session_index.jsonl"),"{\"id\":\"t1\",\"thread_name\":\"真实会话标题\"}\n");
            var first=scanner.Read(now.AddSeconds(1));
            Assert(first.Today.Total==1100&&first.Title=="真实会话标题","Parse usage and title");
            Assert(first.Cost is not null,"Configured model cost");
            Assert(scanner.Read(now.AddSeconds(2)).Today.Total==1100,"Polling must not double count");
            File.AppendAllText(path,Record("r1")+"\n");
            Assert(scanner.Read(now.AddSeconds(3)).Today.Total==1100,"Duplicate response IDs must not double count");
            string partial=Record("r2"); File.AppendAllText(path,partial[..(partial.Length/2)]);
            Assert(scanner.Read(now.AddSeconds(4)).Today.Total==1100,"Partial lines must not count yet");
            File.AppendAllText(path,partial[(partial.Length/2)..]+"\n");
            Assert(scanner.Read(now.AddSeconds(5)).Today.Total==2200,"Partial line must complete on next poll");
            File.AppendAllText(path,"not json\n"+Record("r3",now.AddDays(-1).ToString("O"))+"\n");
            Assert(scanner.Read(now.AddSeconds(6)).Today.Total==2200,"Ignore old events and unrelated malformed lines");
            File.AppendAllText(path,"{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-6.1-sol\"}}\n"+Record("r4")+"\n");
            var switched=scanner.Read(now.AddSeconds(7));
            Assert(switched.Today.Total==3300 && Math.Abs(switched.Cost!.Value-0.0046)<1e-9,"Each response must use its own model, including mid-conversation switches");
            File.AppendAllText(path,"{\"type\":\"turn_context\",\"payload\":{\"model\":\"internal-unknown\"}}\n"+Record("r5")+"\n");
            var partialCost=scanner.Read(now.AddSeconds(8));
            Assert(partialCost.Unpriced==1 && Math.Abs(partialCost.Cost!.Value-0.0046)<1e-9,"Unknown models must be excluded explicitly without hiding the known subtotal");
            Assert(partialCost.DayOutput.Length==24 && partialCost.DayOutput.Sum()==partialCost.Today.Output,"Daily chart must use fixed hourly slots for today's complete output");
            Assert(partialCost.AverageOutputPerMinute>0,"Day average output rate must be available");
            Assert(scanner.Read(now.AddDays(1)).Today.Total==0,"Midnight must reset daily statistics");
            Directory.CreateDirectory("diagnostics"); File.WriteAllText("diagnostics/self-test.txt","PASS: quota duration, missing quota, cached cost, real title, incremental polling, deduplication, partial append, old records, date rollover, auto prices, dated models, long context, mid-conversation switching, unknown model subtotal, real-time orbit positions, quota/time separation, missing/expired windows, fixed daily output chart, concentric circular tracks, unified clockwise direction, flat Earth day/night lighting\n");
        }
        finally { Directory.Delete(temp,true); }
    }
}
