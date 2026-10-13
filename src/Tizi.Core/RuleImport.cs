using System.Text;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed record RuleImport(List<RoutingRule> Rules, Dictionary<string, string> PolicyMappings)
{
    // Only explicit routing statements are imported. Ports, DNS, listeners and
    // scripts from external configurations never replace application settings.
    public static RuleImport Parse(string input)
    {
        if (Encoding.UTF8.GetByteCount(input) > NodeImport.MaxBytes) throw new FormatException("Import is too large (4 MB maximum).");
        var text = input.Trim('\uFEFF', ' ', '\t', '\r', '\n');
        JsonObject? config = text.StartsWith('{') ? JsonNode.Parse(text, documentOptions: new() { MaxDepth = 24 }) as JsonObject : text.Contains("rules:") ? NodeImport.ReadYaml(text) : null;
        var groups = (config?["proxy-groups"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(g => g["name"]?.ToString() ?? "", StringComparer.Ordinal);
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        string Resolve(string name, HashSet<string> active)
        {
            if (name is "TIZI" or "PROXY") return "TIZI";
            if (name is "DIRECT" or "REJECT") return name;
            if (mappings.TryGetValue(name, out var known)) return known;
            if (!groups.TryGetValue(name, out var group)) throw new FormatException("Unknown rule policy. Import a complete Clash configuration or use TIZI / DIRECT / REJECT.");
            if (groups.Count > 100 || !active.Add(name)) throw new FormatException("Circular or excessive proxy groups.");
            if (group["type"]?.ToString() != "select" || group["proxies"] is not JsonArray { Count: > 0 } choices) throw new FormatException("Only select-group defaults can be converted.");
            var first = choices[0]?.ToString() ?? "";
            // A node chosen by a subscription group follows TIZI's current node;
            // it must not replace the user's selected server during rule import.
            var action = groups.ContainsKey(first) || first is "DIRECT" or "REJECT" or "TIZI" or "PROXY" ? Resolve(first, active) : (config?["proxies"] as JsonArray ?? []).Any(n => n?["name"]?.ToString() == first) ? "TIZI" : throw new FormatException("Proxy group refers to an unknown node.");
            active.Remove(name); return mappings[name] = action;
        }
        var lines = config is null ? text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith(';') && !l.StartsWith('[')) : (config["rules"] as JsonArray ?? throw new FormatException("No routing rules in this configuration.")).Select(r => r?.ToString() ?? "");
        var rules = new List<RoutingRule>(); var ended = false;
        foreach (var line in lines)
        {
            if (ended) throw new FormatException("A final rule must be the last rule.");
            var fields = line.Split(',', StringSplitOptions.TrimEntries); var type = fields[0].ToUpperInvariant(); if (type == "FINAL") type = "MATCH";
            var final = type == "MATCH";
            if ((final && fields.Length != 2) || (!final && fields.Length is not (3 or 4))) throw new FormatException("Expected TYPE,value,ACTION or MATCH,ACTION.");
            var action = Resolve(fields[final ? 1 : 2], []);
            var rule = new RoutingRule(type, final ? "" : fields[1], action) { NoResolve = fields.Length == 4 && fields[3].Equals("no-resolve", StringComparison.OrdinalIgnoreCase) };
            if (fields.Length == 4 && !rule.NoResolve) throw new FormatException("Unsupported rule option.");
            ModelValidation.Rule(rule); rules.Add(rule); ended = final;
            if (rules.Count > 10000) throw new FormatException("Maximum 10000 rules.");
        }
        if (rules.Count == 0) throw new FormatException("No routing rules in this configuration.");
        return new(rules, mappings);
    }
    public static List<RoutingRule> Merge(IReadOnlyList<RoutingRule> previous, IReadOnlyList<RoutingRule> imported)
    {
        // Preserve user overrides ahead of an imported final policy. Repeating
        // the same import is idempotent, including the single final rule.
        var rules = previous.Where(r => r.Type != "MATCH").Concat(imported.Where(r => r.Type != "MATCH")).Distinct().ToList();
        if ((imported.LastOrDefault(r => r.Type == "MATCH") ?? previous.LastOrDefault(r => r.Type == "MATCH")) is { } final) rules.Add(final);
        if (rules.Count > 10000) throw new FormatException("Maximum 10000 rules.");
        return rules;
    }
}
