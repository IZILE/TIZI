using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class RuleImportTests
{
    private const string Configuration = """
        proxies:
          - {name: Tokyo, type: ss, server: example.net, port: 443, cipher: aes-128-gcm, password: demo}
        proxy-groups:
          - {name: Main, type: select, proxies: [Tokyo, DIRECT]}
          - {name: Local, type: select, proxies: [DIRECT, Main]}
          - {name: Ads, type: select, proxies: [REJECT, DIRECT]}
          - {name: Fallback, type: select, proxies: [Main, DIRECT]}
        rules:
          - DOMAIN-SUFFIX,example.cn,Local
          - DOMAIN,ads.example.net,Ads
          - IP-CIDR,192.0.2.0/24,Main,no-resolve
          - IP-CIDR6,2001:db8::/32,Main,no-resolve
          - GEOIP,CN,Local,no-resolve
          - MATCH,Fallback
        """;
    [Fact] public void FullClashRulesPreserveOrderDefaultPoliciesAndNoResolve()
    {
        var parsed = RuleImport.Parse(Configuration);
        Assert.Equal(new[] { "DIRECT", "REJECT", "TIZI", "TIZI", "DIRECT", "TIZI" }, parsed.Rules.Select(r => r.Action));
        Assert.Equal("TIZI", parsed.PolicyMappings["Fallback"]);
        Assert.Equal(3, parsed.Rules.Count(r => r.NoResolve));
        var config = JsonNode.Parse(EngineSession.Create(new AppState { Rules = parsed.Rules, Mode = RouteMode.Rule, Capture = CaptureMode.LocalOnly }, 23456).Config)!;
        Assert.Equal(parsed.Rules.Select(r => r.ToCore()), config["rules"]!.AsArray().Select(r => r!.ToString()));
        Assert.True(config["geodata-mode"]!.GetValue<bool>());
        Assert.False(config["geo-auto-update"]!.GetValue<bool>());
    }
    [Fact] public void RepeatedImportIsIdempotentAndKeepsUserOverrideBeforeFinal()
    {
        var incoming = RuleImport.Parse(Configuration).Rules;
        var user = new RoutingRule("DOMAIN", "override.example", "DIRECT");
        var once = RuleImport.Merge([user, new("MATCH", "", "DIRECT")], incoming);
        var twice = RuleImport.Merge(once, incoming);
        Assert.Equal(once, twice); Assert.Equal(user, once[0]); Assert.Equal("TIZI", once[^1].Action);
        var added = RuleImport.Merge(once, [new("DOMAIN", "another.example", "REJECT")]);
        Assert.Equal("MATCH", added[^1].Type); ModelValidation.Rules(added);
    }
    [Theory]
    [InlineData("DOMAIN,example.com,DIRECT\nMATCH,TIZI\nDOMAIN,late.example,REJECT")]
    [InlineData("IP-CIDR,2001:db8::/32,DIRECT")]
    [InlineData("IP-CIDR6,192.0.2.0/24,DIRECT")]
    [InlineData("DOMAIN,example.com,TIZI,no-resolve")]
    [InlineData("DOMAIN,example.com,Unknown")]
    [InlineData("GEOIP,CN,TIZI,unsupported")]
    public void InvalidOrUnreachableRulesNeverProducePartialImports(string text)
        => Assert.Throws<FormatException>(() => RuleImport.Parse(text));
    [Fact] public void CyclicPoliciesAreRejected()
    {
        const string config = "proxy-groups:\n  - {name: A, type: select, proxies: [B]}\n  - {name: B, type: select, proxies: [A]}\nrules:\n  - MATCH,A";
        Assert.Throws<FormatException>(() => RuleImport.Parse(config));
    }
    [Fact] public void ShadowrocketFinalAliasConvertsAndModelRejectsEarlyFinal()
    {
        Assert.Equal("MATCH,DIRECT", RuleImport.Parse("[Rule]\n# comment\nDOMAIN,example.com,TIZI\nFINAL,DIRECT").Rules[^1].ToCore());
        Assert.Throws<FormatException>(() => EngineSession.Create(new AppState { Rules = [new("MATCH", "", "TIZI"), new("DOMAIN", "late.example", "DIRECT")] }, 23456));
    }
}
