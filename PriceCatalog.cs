using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodexIsland;
public static class PriceCatalog
{
    public const string Verified="2026-10-22";
    public const string Source="https://developers.openai.com/api/docs/pricing";
    // Standard API-equivalent rates per million tokens. Never guess internal model prices.
    private static readonly Dictionary<string,Price> Rates=new(StringComparer.Ordinal)
    {
        ["gpt-6.1-sol"]=new(2,0.1,10),
        ["gpt-6-sol"]=new(2,0.2,10),
        ["gpt-6-astra"]=new(10,1,50),
        ["gpt-6-luna"]=new(0.1,0.01,0.5),
        ["gpt-5.6-luna"]=new(0.2,0.02,1.2),
        ["gpt-5.6-sol"]=new(4,0.4,20),
        ["gpt-5.3-codex"]=new(1.75,0.175,14)
    };
    public static string Normalize(string model)=>Regex.Replace(model,@"-\d{4}-\d{2}-\d{2}$","");
    public static Price? Resolve(string model,Settings settings)
    {
        if(settings.Prices.TryGetValue(model,out var custom)) return custom;
        return Rates.GetValueOrDefault(Normalize(model));
    }
    public static double Estimate(Usage usage,Price price,string model)
    {
        string name=Normalize(model);
        bool longContext=usage.Input>272000 && (name is "gpt-6.1-sol" or "gpt-6-sol" or "gpt-5.6-luna");
        if(longContext) price=new(price.Input*2,price.CachedInput*2,price.Output*1.5);
        return LocalTelemetry.Estimate(usage,price);
    }
}
