namespace Tizi.Core;

/// <summary>Recover subscription contents without replacing current preferences or credentials.</summary>
public static class ProfileRecovery
{
    public static AppState MergeContents(AppState current, AppState backup)
    {
        var subscriptions = current.Subscriptions.ToList(); var remap = new Dictionary<Guid, Guid>();
        foreach (var subscription in backup.Subscriptions)
        {
            var existing = subscriptions.FirstOrDefault(s => s.Id == subscription.Id || SourceUpdates.Normalize(s.Url) == SourceUpdates.Normalize(subscription.Url));
            if (existing is null) { subscriptions.Add(subscription); remap[subscription.Id] = subscription.Id; }
            else remap[subscription.Id] = existing.Id;
        }
        var nodes = current.Nodes.ToList(); var ids = nodes.Select(n => n.Id).ToHashSet();
        foreach (var node in backup.Nodes)
        {
            if (!ids.Add(node.Id)) continue;
            // A manual QR import can receive a new ID after an old install. Keep
            // the current copy when its actual protocol configuration is identical.
            if (node.SubscriptionId is null && nodes.Any(n => n.SubscriptionId is null && SameManualConfiguration(n, node))) continue;
            nodes.Add(node with { SubscriptionId = node.SubscriptionId is { } id && remap.TryGetValue(id, out var mapped) ? mapped : node.SubscriptionId });
        }
        if (nodes.Count > NodeImport.MaxNodes) throw new FormatException("Maximum 3000 nodes.");
        if (subscriptions.Count > 100) throw new FormatException("Maximum 100 subscriptions.");
        var rules = RuleImport.Merge(current.Rules, backup.Rules);
        ModelValidation.Rules(rules);
        return current with
        {
            Nodes = nodes, Subscriptions = subscriptions, Rules = rules,
            SelectedNode = nodes.Any(n => n.Id == current.SelectedNode) ? current.SelectedNode : nodes.FirstOrDefault()?.Id,
            RuleSourceName = current.Rules.Count > 0 ? current.RuleSourceName : backup.RuleSourceName,
            RulesUpdatedAt = current.Rules.Count > 0 ? current.RulesUpdatedAt : backup.RulesUpdatedAt
        };
    }
    private static bool SameManualConfiguration(ProxyNode first, ProxyNode second)
    {
        var a = first.Options.DeepClone().AsObject(); var b = second.Options.DeepClone().AsObject(); a.Remove("name"); b.Remove("name");
        return System.Text.Json.Nodes.JsonNode.DeepEquals(a, b);
    }
}
