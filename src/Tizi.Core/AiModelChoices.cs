using System.Text.RegularExpressions;

namespace Tizi.Core;

/// <summary>A concise catalog for public-page research, rather than every inference task.</summary>
public static class AiModelChoices
{
    private static readonly Regex Specialized = new(@"(?:^|[-_/])(?:audio|realtime|live|speech|tts|asr|whisper|transcrib\w*|diariz\w*|embed\w*|rerank\w*|moderation|image|imagen|dall|sora|veo|video|ocr|codex|coder|code|computer|cyber|rosalind|character|math|mt)(?:$|[-_/])",RegexOptions.IgnoreCase);
    private static readonly Regex Snapshot = new(@"-(?:20\d{2}[-_]\d{2}[-_]\d{2}|20\d{6}|\d{4})$",RegexOptions.IgnoreCase);
    private sealed record Choice(string Id,string Base,string Family,string Flavor,Version Version,bool Alias,string Snapshot);

    public static List<string> ForSearch(string provider,IEnumerable<string> models)
    {
        var candidates=models.Where(id=>id is {Length:>0 and <=80}&&!id.Any(char.IsControl)&&!Specialized.IsMatch(id))
            .Distinct(StringComparer.Ordinal).Select(id=>Parse(id,provider)).Where(c=>c is not null).Cast<Choice>().ToList();
        var familyName=provider=="OpenAI"?"gpt":provider.ToLowerInvariant();
        if(provider!="Custom")candidates=candidates.Where(c=>c.Family==familyName).ToList();
        var selected=new List<Choice>();
        foreach(var family in candidates.GroupBy(c=>c.Family))
        {
            var entries=family.ToList();
            if(family.Key is "gpt" or "gemini" or "glm")
            {
                var generation=entries.Max(c=>c.Version.Major);
                entries=entries.Where(c=>c.Version.Major==generation).ToList();
            }
            if(family.Key=="kimi"){var newest=entries.Max(c=>c.Version);entries=entries.Where(c=>c.Version.Major>=2&&c.Version==newest).ToList();}
            if(family.Key=="deepseek"&&entries.Any(c=>c.Flavor is "flash" or "pro"))entries=entries.Where(c=>c.Flavor is "flash" or "pro").ToList();
            foreach(var flavor in entries.GroupBy(c=>c.Flavor))
            {
                var newest=flavor.Max(c=>c.Version);
                var options=flavor.Where(c=>c.Version==newest).ToList();
                selected.Add(options.OrderByDescending(c=>c.Alias).ThenBy(c=>c.Id.EndsWith("-preview",StringComparison.OrdinalIgnoreCase)).ThenByDescending(c=>c.Snapshot,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).First());
            }
        }
        return selected.OrderBy(c=>c.Family).ThenBy(c=>c.Flavor).Select(c=>c.Id).Take(12).ToList();
    }

    private static Choice? Parse(string id,string provider)
    {
        var lower=id.ToLowerInvariant();var stamp=Snapshot.Match(lower);var basis=stamp.Success?lower[..stamp.Index]:lower;
        var stable=basis.EndsWith("-latest")?basis[..^7]:basis;
        if(stable.EndsWith("-preview"))stable=stable[..^8];
        Match match;
        Choice? Build(string family,string flavor,string version,bool alias=false)=>Version.TryParse(version.Contains('.')?version:version+".0",out var number)&&number.Major is >=0 and <=99&&number.Minor<=99?new(id,stable,family,flavor,number,alias,stamp.Success?stamp.Value:""):null;
        if((match=Regex.Match(stable,@"^(?:gpt|chatgpt)-(\d+(?:\.\d+)?)(?:-(astra|sol|terra|luna|mini|nano|pro|chat))?$")).Success)
        {var choice=Build("gpt",match.Groups[2].Success?match.Groups[2].Value:"main",match.Groups[1].Value,!stamp.Success);return choice is null||choice.Version.Major<5||choice.Version.Major==5&&choice.Version.Minor<4?null:choice;}
        if(Regex.IsMatch(stable,@"^(?:gpt|chatgpt)-(?:3|4)(?:[.o-]|$)|^o[134](?:-|$)|^(?:babbage|davinci|text-davinci|curie|ada)(?:-|$)"))return null;
        if((match=Regex.Match(stable,@"^qwen(\d+(?:\.\d+)?)?-(plus|max|flash|turbo|long)$")).Success)
            return match.Groups[2].Value is "turbo" or "long"?null:Build("qwen",match.Groups[2].Value,match.Groups[1].Success?match.Groups[1].Value:"3",!stamp.Success);
        if(stable.StartsWith("qwen"))return null;
        if((match=Regex.Match(stable,@"^deepseek-(?:v(\d+(?:\.\d+)?)-)?(flash|pro|chat|reasoner)$")).Success)
            return Build("deepseek",match.Groups[2].Value,match.Groups[1].Success?match.Groups[1].Value:"4",!stamp.Success);
        if(stable.StartsWith("deepseek-"))return null;
        if((match=Regex.Match(stable,@"^kimi-k(\d+(?:\.\d+)?)(?:-(thinking|instant|turbo))?$")).Success)
            return Build("kimi",match.Groups[2].Success?match.Groups[2].Value:"main",match.Groups[1].Value,!stamp.Success);
        if(stable.StartsWith("moonshot-v1")||stable.StartsWith("kimi-"))return null;
        if((match=Regex.Match(stable,@"^glm-(\d+(?:\.\d+)?)(?:-(plus|flash|air|flashx|z1))?$")).Success)
            return Build("glm",match.Groups[2].Success?match.Groups[2].Value:"main",match.Groups[1].Value,!stamp.Success);
        if(stable.StartsWith("glm-"))return null;
        if((match=Regex.Match(stable,@"^gemini-(\d+(?:\.\d+)?)-(pro|flash|flash-lite)$")).Success)
            return Build("gemini",match.Groups[2].Value,match.Groups[1].Value,!stamp.Success);
        if(stable.StartsWith("gemini-"))return null;
        if((match=Regex.Match(stable,@"^claude-(opus|sonnet|haiku)-(\d+(?:[-.]\d+)?)$")).Success)
        {var choice=Build("claude",match.Groups[1].Value,match.Groups[2].Value.Replace('-','.'),!stamp.Success);return choice?.Version.Major>=4?choice:null;}
        if((match=Regex.Match(stable,@"^claude-(\d+(?:[-.]\d+)?)-(opus|sonnet|haiku)$")).Success)
        {var choice=Build("claude",match.Groups[2].Value,match.Groups[1].Value.Replace('-','.'),!stamp.Success);return choice?.Version.Major>=4?choice:null;}
        if(stable.StartsWith("claude-"))return null;
        // Unknown gateway aliases are kept without claiming their age or availability.
        return provider=="Custom"?new(id,stable,"custom:"+stable,"main",new(0,0),!stamp.Success,stamp.Success?stamp.Value:""):null;
    }
}
