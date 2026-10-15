using System.Text.RegularExpressions;

namespace Tizi.Core;

/// <summary>A concise catalog for public-page research, rather than every inference task.</summary>
public static class AiModelChoices
{
    private static readonly Regex Specialized = new(@"(?:^|[-_/])(?:audio|realtime|live|voice|speech|tts|asr|whisper|transcrib\w*|diariz\w*|embed\w*|rerank\w*|moderation|image|imagen|dall|sora|veo|video|ocr|codex|coder|code|computer|cyber|rosalind|character|math|mt)(?:$|[-_/])",RegexOptions.IgnoreCase);
    private static readonly Regex Snapshot = new(@"-(?:20\d{2}[-_]\d{2}[-_]\d{2}|20\d{6}|\d{4})$",RegexOptions.IgnoreCase);
    private sealed record Choice(string Id,string Base,string Family,string Flavor,Version Version,bool Alias,string Snapshot);

    public static List<string> ForSearch(string provider,IEnumerable<string> models,string? preferredModel=null)
    {
        var candidates=models.Where(id=>id is {Length:>0 and <=80}&&!id.Any(char.IsControl)&&!Specialized.IsMatch(id))
            .Distinct(StringComparer.Ordinal).Select(id=>Parse(id,provider)).Where(c=>c is not null).Cast<Choice>().ToList();
        var familyName=provider=="OpenAI"?"gpt":provider.ToLowerInvariant();
        if(provider!="Custom")candidates=candidates.Where(c=>c.Family==familyName).ToList();
        var defaultModel=AiApiProfiles.Presets.FirstOrDefault(p=>p.Name==provider)?.Model;
        var selected=candidates.GroupBy(c=>(c.Family,c.Flavor)).Select(flavor=>flavor
            .OrderByDescending(c=>c.Version).ThenByDescending(c=>c.Id==preferredModel).ThenByDescending(c=>c.Id==defaultModel)
            .ThenByDescending(c=>c.Alias).ThenBy(c=>c.Id.Contains("-preview",StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c=>c.Snapshot,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).First())
            .OrderBy(c=>c.Family,StringComparer.Ordinal).ThenBy(c=>c.Flavor,StringComparer.Ordinal);
        // A newer flagship must not remove a lower-cost default or a valid saved selection.
        // Pin only IDs actually present in the supplied catalog; never invent availability.
        var pinned=new[]{preferredModel,defaultModel}.Select(id=>candidates.FirstOrDefault(c=>c.Id==id)).OfType<Choice>();
        return pinned.Concat(selected).DistinctBy(c=>(c.Family,c.Flavor,c.Version)).Take(12)
            .OrderBy(c=>c.Family,StringComparer.Ordinal).ThenBy(c=>c.Flavor,StringComparer.Ordinal)
            .ThenByDescending(c=>c.Version).ThenBy(c=>c.Id,StringComparer.Ordinal).Select(c=>c.Id).ToList();
    }

    private static Choice? Parse(string id,string provider)
    {
        var stable=id.ToLowerInvariant();var snapshot="";var preview=false;
        // Providers place dates either before or after the preview suffix.
        while(true)
        {
            if(stable.EndsWith("-latest")){stable=stable[..^7];continue;}
            if(stable.EndsWith("-preview")){stable=stable[..^8];preview=true;continue;}
            var stamp=Snapshot.Match(stable);
            if(stamp.Success){snapshot=stamp.Value;stable=stable[..stamp.Index];continue;}
            break;
        }
        var alias=snapshot.Length==0&&!preview;
        Match match;
        Choice? Build(string family,string flavor,string version)=>Version.TryParse(version.Contains('.')?version:version+".0",out var number)&&number.Major is >=0 and <=99&&number.Minor<=99?new(id,stable,family,flavor,number,alias,snapshot):null;
        if((match=Regex.Match(stable,@"^(?:gpt|chatgpt)-(\d+(?:\.\d+)?)(?:-(astra|sol|terra|luna|mini|nano|pro|chat))?$")).Success)
        {var choice=Build("gpt",match.Groups[2].Success?match.Groups[2].Value:"main",match.Groups[1].Value);return choice is null||choice.Version.Major<5||choice.Version.Major==5&&choice.Version.Minor<4||stable=="gpt-5.4-nano"?null:choice;}
        if(Regex.IsMatch(stable,@"^(?:gpt|chatgpt)-(?:3|4)(?:[.o-]|$)|^o[134](?:-|$)|^(?:babbage|davinci|text-davinci|curie|ada)(?:-|$)"))return null;
        if((match=Regex.Match(stable,@"^qwen(\d+(?:\.\d+)?)?-(plus|max|flash|turbo|long)$")).Success)
        {var choice=Build("qwen",match.Groups[2].Value,match.Groups[1].Success?match.Groups[1].Value:"3");return match.Groups[2].Value is "turbo" or "long"||choice?.Version.Major<3?null:choice;}
        if(stable.StartsWith("qwen"))return null;
        if((match=Regex.Match(stable,@"^deepseek-(?:v(\d+(?:\.\d+)?)-)?(flash|pro|chat|reasoner)$")).Success)
            return Build("deepseek",match.Groups[2].Value,match.Groups[1].Success?match.Groups[1].Value:"4");
        if(stable.StartsWith("deepseek-"))return null;
        if((match=Regex.Match(stable,@"^kimi-k(\d+(?:\.\d+)?)(?:-(thinking(?:-turbo)?|instant|turbo))?$")).Success)
        {var choice=Build("kimi",match.Groups[2].Success?match.Groups[2].Value:"main",match.Groups[1].Value);return choice?.Version>=new Version(2,6)?choice:null;}
        if(stable.StartsWith("moonshot-v1")||stable.StartsWith("kimi-"))return null;
        if((match=Regex.Match(stable,@"^glm-(\d+(?:\.\d+)?)(?:-(plus|flash|air|flashx|z1))?$")).Success)
        {var choice=Build("glm",match.Groups[2].Success?match.Groups[2].Value:"main",match.Groups[1].Value);return choice?.Version.Major>=4?choice:null;}
        if(stable.StartsWith("glm-"))return null;
        if((match=Regex.Match(stable,@"^gemini-(\d+(?:\.\d+)?)-(pro|flash|flash-lite|argon)$")).Success)
        {var choice=Build("gemini",match.Groups[2].Value,match.Groups[1].Value);return choice?.Version>=new Version(2,5)?choice:null;}
        if(stable.StartsWith("gemini-"))return null;
        if((match=Regex.Match(stable,@"^claude-(opus|sonnet|haiku)-(\d+(?:[-.]\d+)?)$")).Success)
        {var choice=Build("claude",match.Groups[1].Value,match.Groups[2].Value.Replace('-','.'));return choice?.Version.Major>=4?choice:null;}
        if((match=Regex.Match(stable,@"^claude-(\d+(?:[-.]\d+)?)-(opus|sonnet|haiku)$")).Success)
        {var choice=Build("claude",match.Groups[2].Value,match.Groups[1].Value.Replace('-','.'));return choice?.Version.Major>=4?choice:null;}
        if(stable.StartsWith("claude-"))return null;
        // Unknown gateway aliases are kept without claiming their age or availability.
        return provider=="Custom"?new(id,stable,"custom:"+stable,"main",new(0,0),alias,snapshot):null;
    }
}
