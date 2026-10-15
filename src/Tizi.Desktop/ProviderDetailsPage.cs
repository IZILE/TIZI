using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed class ProviderDetailsPage : SecondPage
{
    internal Guid ProviderId { get; }
    internal ProviderDetailsPage(ProviderSite site, string currency, Action<string> open)
    {
        ProviderId = site.Id; Tag = "provider-details";
        Margin = new(0,0,-Ui.PageScrollGutter,0);
        ClipToBounds = true;
        var body = new StackPanel { Spacing = 24 };
        var name = Wrap(site.Name, 25); name.FontWeight = FontWeight.SemiBold;
        var heading = new StackPanel { Spacing = 9, Children = { name, Wrap(new Uri(site.Url).Host, 13, true) } };
        var profile = site.Profile;
        if (profile is not null)
        {
            if (profile.Description.Length > 0) heading.Children.Add(Wrap(profile.Description, 14));
            var meta = string.Join("  ·  ", new[] { string.Join(" / ", profile.Regions), string.Join(" / ", profile.SupportedProtocols) }.Where(s => s.Length > 0));
            if (meta.Length > 0) heading.Children.Add(Wrap(meta, 13, true));
        }
        body.Children.Add(heading);
        if (profile is not null)
        {
            var plans = new StackPanel { Spacing = 10 }; plans.Children.Add(SectionTitle("Plans and prices"));
            var planCards = new SettingsCards();
            var first = ProviderDirectory.LowestPlan(site, currency);
            foreach (var plan in profile.Plans.OrderBy(p => p == first ? 0 : 1))
            {
                var price = Wrap(UiFormat.Plan(plan), 22); price.Foreground = Ui.DataAccent; price.FontWeight = FontWeight.SemiBold;
                var content = new StackPanel { Spacing = 9, Children = { Wrap(plan.Name, 15), price } };
                if (plan.Period is "quarter" or "year" && ProviderDiscovery.MonthlyEquivalent(plan) is not null)
                    content.Children.Add(Wrap(UiText.Format("{0} · actual payment above", UiFormat.ComparisonPrice(plan)), 13, true));
                if (plan.TrafficGb is { } quota) content.Children.Add(Wrap(UiText.Format("Traffic quota · {0} GB", quota.ToString("0.##")), 13));
                if (plan.Conditions.Length > 0) content.Children.Add(Wrap(plan.Conditions, 13, true));
                var source = Ui.Button("View official pricing", () => open(plan.Source)); source.HorizontalAlignment = HorizontalAlignment.Left; source.MinHeight = 30; source.Padding = new(12, 4);
                content.Children.Add(source);
                planCards.Children.Add(new Border { Padding = new(18), CornerRadius = new(12), Background = Skin.ChromeBrush, Child = content });
            }
            plans.Children.Add(planCards);
            if (profile.Plans.Count == 0) plans.Children.Add(Wrap(UiText.Get("Price unavailable"), 14, true));
            body.Children.Add(plans);
            if (profile.ReviewSummary.Length > 0)
                body.Children.Add(new StackPanel { Spacing = 10, Children = { SectionTitle("User reviews and rankings"), Wrap(profile.ReviewSummary, 14) } });
            var reviews = new StackPanel { Spacing = 16 };
            foreach (var review in profile.Reviews)
            {
                var title = Ui.Button(review.Title, () => open(review.Url)); title.HorizontalAlignment = HorizontalAlignment.Left; title.HorizontalContentAlignment = HorizontalAlignment.Left;
                title.Content = Wrap(review.Title, 14); title.Padding = new(0); title.Classes.Add("plain");
                var attribution = new Uri(review.Url).Host + " · " + (review.Date ?? UiText.Get("Date unknown"))
                    + (review.Rank is { } rank ? " · " + UiText.Format("Rank {0} in this list", rank) : "");
                reviews.Children.Add(new StackPanel { Spacing = 6, Children = { title, Wrap(attribution, 12, true), Wrap(review.Summary, 13, true) } });
            }
            if (profile.Reviews.Count > 0) body.Children.Add(Disclosure(UiText.Format("Review sources ({0})", profile.Reviews.Count), reviews));
            var sources = new StackPanel { Spacing = 8 };
            foreach (var url in profile.Sources.Distinct())
            {
                var link = Ui.Button(new Uri(url).Host, () => open(url)); link.HorizontalAlignment = HorizontalAlignment.Left; ToolTip.SetTip(link, url); sources.Children.Add(link);
            }
            if (profile.Sources.Count > 0) body.Children.Add(Disclosure(UiText.Get("Official sources"), sources));
            body.Children.Add(Wrap(UiText.Get("Researched") + " · " + profile.ResearchedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 12, true));
        }
        Child = Ui.Scroll(body, overlayScrollbar: true);
    }
    private static TextBlock Wrap(string text, double size, bool muted = false)
    { var block = Ui.Value(text, muted, size); block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.None; block.LineHeight = size * 1.55; return block; }
    private static TextBlock SectionTitle(string key) { var title = Ui.Text(key, size: 17); title.FontWeight = FontWeight.SemiBold; return title; }
    private static Control Disclosure(string label, Control content)
    {
        var reveal = new AnimatedReveal { Child = content, Progress = 0, IsHitTestVisible = false };
        var button = Ui.Button(label + "  +", () => { }); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Classes.Add("plain");
        var expanded = false;
        button.Click += (_, _) => { expanded = !expanded; button.Content = Ui.Value(label + (expanded ? "  −" : "  +")); reveal.IsHitTestVisible = expanded; Motion.Set(reveal, AnimatedReveal.ProgressProperty, expanded ? 1d : 0d); };
        return new StackPanel { Spacing = 10, Children = { button, reveal } };
    }
}
