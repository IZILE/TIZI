using Tizi.Core;

namespace Tizi.Tests;

public sealed class AiModelChoicesTests
{
    [Theory]
    [InlineData("OpenAI","gpt-4o,gpt-5.4-mini,gpt-6-sol,gpt-6.1-sol,gpt-6.1-sol-20261016,gpt-6-astra,gpt-6-luna,gpt-live-1,gpt-audio-1.5,gpt-image-2.5,whisper-1,text-embedding-3-large","gpt-6-astra,gpt-6-luna,gpt-5.4-mini,gpt-6.1-sol")]
    [InlineData("DeepSeek","deepseek-chat,deepseek-reasoner,deepseek-v3-chat,deepseek-flash,deepseek-v4-pro,deepseek-v4-flash-0731","deepseek-chat,deepseek-flash,deepseek-v4-pro,deepseek-reasoner")]
    [InlineData("Qwen","qwen-plus,qwen3.5-plus,qwen3.7-plus,qwen3.7-plus-2026-05-26,qwen3.8-flash,qwen3.8-max,qwen-turbo,qwen3-235b-a22b,qwen3-tts-flash,qwen3-rerank,qwen-coder-plus,gpt-6-sol,deepseek-flash","qwen3.8-flash,qwen3.8-max,qwen3.7-plus,qwen-plus")]
    [InlineData("Kimi","moonshot-v1-8k,moonshot-v1-32k,kimi-k2-0905-preview,kimi-k2-thinking-turbo,kimi-k2.5,kimi-k2.6,kimi-k3,kimi-k2.7-code","kimi-k3,kimi-k2.6")]
    [InlineData("GLM","glm-4,glm-4-flash,glm-4.7,glm-5,glm-5.2,glm-5.2-flash,glm-4-voice","glm-5.2-flash,glm-4-flash,glm-5.2")]
    [InlineData("Gemini","gemini-1.5-pro,gemini-2.0-flash,gemini-2.5-flash,gemini-3.1-flash,gemini-3.1-pro-preview,gemini-3.1-pro,gemini-3.1-pro-2026-10-01,gemini-2.5-flash-native-audio,imagen-4.0,veo-3.1","gemini-3.1-flash,gemini-2.5-flash,gemini-3.1-pro")]
    [InlineData("Claude","claude-3-7-sonnet-latest,claude-sonnet-4-5,claude-sonnet-4-5-20250929,claude-sonnet-4-6,claude-opus-4-6,claude-opus-4-7,claude-haiku-4-5,claude-haiku-4-5-20251001","claude-haiku-4-5,claude-opus-4-7,claude-sonnet-4-6,claude-sonnet-4-5")]
    [InlineData("Custom","gateway-chat,gateway-chat-2026-01-01,gateway-audio,gateway-embedding,gateway-chat","gateway-chat")]
    public void SearchCatalogKeepsCurrentGeneralTextFamiliesAndCollapsesSnapshots(string provider,string input,string expected)
        =>Assert.Equal(expected.Split(','),AiModelChoices.ForSearch(provider,input.Split(',')));

    [Fact] public void AStoredVoiceOrLegacySelectionCannotBeAddedBackToTheMenu()
    {
        Assert.Equal(new[]{"gpt-6-sol"},AiModelChoices.ForSearch("OpenAI",new[]{"gpt-6-sol","gpt-4o","gpt-audio"}));
        Assert.Empty(AiModelChoices.ForSearch("OpenAI",new[]{"whisper-1","gpt-4o","gpt-3.5-turbo"}));
    }

    [Theory]
    [InlineData("OpenAI","gpt-5.4-mini","gpt-6.1-mini")]
    [InlineData("DeepSeek","deepseek-flash","deepseek-v5-flash")]
    [InlineData("Qwen","qwen-plus","qwen3.7-plus")]
    [InlineData("Kimi","kimi-k2.6","kimi-k3")]
    [InlineData("GLM","glm-4-flash","glm-5.2-flash")]
    [InlineData("Gemini","gemini-2.5-flash","gemini-3.8-flash")]
    [InlineData("Claude","claude-sonnet-4-5","claude-sonnet-4-6")]
    public void NewerModelsDoNotRemoveTheAvailableDefault(string provider,string baseline,string newer)
    {
        Assert.Equal(baseline,AiApiProfiles.Presets.Single(p=>p.Name==provider).Model);
        var choices=AiModelChoices.ForSearch(provider,new[]{baseline,newer});
        Assert.Contains(baseline,choices);Assert.Contains(newer,choices);
        Assert.DoesNotContain(baseline,AiModelChoices.ForSearch(provider,new[]{newer}));
    }

    [Fact] public void AValidSavedModelSurvivesNewerModelsAndTheMenuLimit()
    {
        var catalog=Enumerable.Range(0,20).Select(i=>"gateway-"+i).Append("gpt-5.4-mini").Append("gpt-6.1-mini").ToArray();
        var choices=AiModelChoices.ForSearch("Custom",catalog,"gpt-5.4-mini");
        Assert.Equal(12,choices.Count);Assert.Contains("gpt-5.4-mini",choices);
        Assert.Equal(choices,AiModelChoices.ForSearch("Custom",catalog.Reverse(),"gpt-5.4-mini"));
        Assert.DoesNotContain("gpt-5.4-mini",AiModelChoices.ForSearch("OpenAI",new[]{"gpt-6.1-mini"},"gpt-5.4-mini"));
    }

    [Theory]
    [InlineData("OpenAI","gpt-5.4-mini-20261016")]
    [InlineData("Kimi","kimi-k2.6")]
    [InlineData("Gemini","gemini-2.5-pro")]
    [InlineData("GLM","glm-4.7-flash")]
    public void AnAvailableSavedTierIsKeptWithoutDuplicateSnapshots(string provider,string selected)
    {
        var catalog=new[]{selected,"gpt-5.4-mini","gpt-6.1-mini","kimi-k3","gemini-3.1-pro","glm-5.2-flash"};
        var choices=AiModelChoices.ForSearch(provider,catalog,selected);
        Assert.Contains(selected,choices);
        if(provider=="OpenAI")Assert.DoesNotContain("gpt-5.4-mini",choices);
    }

    [Theory]
    [InlineData("OpenAI","gpt-5.4-nano")]
    [InlineData("Kimi","moonshot-v1-32k")]
    [InlineData("Kimi","kimi-k2.5")]
    [InlineData("Kimi","kimi-k2-thinking-turbo")]
    [InlineData("Gemini","gemini-2.0-flash")]
    [InlineData("Custom","gateway-voice")]
    public void PinningCannotRestoreRetiredOrSpecializedModels(string provider,string selected)
        =>Assert.Empty(AiModelChoices.ForSearch(provider,new[]{selected},selected));

    [Fact] public void PreviewDateOrderDoesNotCreateExtraSnapshotChoices()
        =>Assert.Equal(new[]{"kimi-k2.6"},AiModelChoices.ForSearch("Kimi",new[]{"kimi-k2.6-1016-preview","kimi-k2.6-preview-1016","kimi-k2.6"}));
}
