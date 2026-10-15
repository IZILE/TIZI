using Tizi.Core;

namespace Tizi.Tests;

public sealed class AiModelChoicesTests
{
    [Theory]
    [InlineData("OpenAI","gpt-4o,gpt-5.4-mini,gpt-6-sol,gpt-6.1-sol,gpt-6.1-sol-20261016,gpt-6-astra,gpt-6-luna,gpt-live-1,gpt-audio-1.5,gpt-image-2.5,whisper-1,text-embedding-3-large","gpt-6-astra,gpt-6-luna,gpt-6.1-sol")]
    [InlineData("DeepSeek","deepseek-chat,deepseek-reasoner,deepseek-v3-chat,deepseek-flash,deepseek-v4-pro,deepseek-v4-flash-0731","deepseek-flash,deepseek-v4-pro")]
    [InlineData("Qwen","qwen-plus,qwen3.5-plus,qwen3.7-plus,qwen3.7-plus-2026-05-26,qwen3.8-flash,qwen3.8-max,qwen-turbo,qwen3-235b-a22b,qwen3-tts-flash,qwen3-rerank,qwen-coder-plus,gpt-6-sol,deepseek-flash","qwen3.8-flash,qwen3.8-max,qwen3.7-plus")]
    [InlineData("Kimi","moonshot-v1-8k,moonshot-v1-32k,kimi-k2,kimi-k2-thinking,kimi-k2.5,kimi-k2.7,kimi-k2.7-code","kimi-k2.7")]
    [InlineData("GLM","glm-4,glm-4-flash,glm-4.7,glm-5,glm-5.2,glm-5.2-flash,glm-4-voice","glm-5.2-flash,glm-5.2")]
    [InlineData("Gemini","gemini-1.5-pro,gemini-2.5-flash,gemini-3.1-flash,gemini-3.1-pro-preview,gemini-3.1-pro,gemini-3.1-pro-2026-10-01,gemini-2.5-flash-native-audio,imagen-4.0,veo-3.1","gemini-3.1-flash,gemini-3.1-pro")]
    [InlineData("Claude","claude-3-7-sonnet-latest,claude-sonnet-4-5-20250929,claude-sonnet-4-6,claude-opus-4-6,claude-opus-4-7,claude-haiku-4-5,claude-haiku-4-5-20251001","claude-haiku-4-5,claude-opus-4-7,claude-sonnet-4-6")]
    [InlineData("Custom","gateway-chat,gateway-chat-2026-01-01,gateway-audio,gateway-embedding,gateway-chat","gateway-chat")]
    public void SearchCatalogKeepsCurrentGeneralTextFamiliesAndCollapsesSnapshots(string provider,string input,string expected)
        =>Assert.Equal(expected.Split(','),AiModelChoices.ForSearch(provider,input.Split(',')));

    [Fact] public void AStoredVoiceOrLegacySelectionCannotBeAddedBackToTheMenu()
    {
        Assert.Equal(new[]{"gpt-6-sol"},AiModelChoices.ForSearch("OpenAI",new[]{"gpt-6-sol","gpt-4o","gpt-audio"}));
        Assert.Empty(AiModelChoices.ForSearch("OpenAI",new[]{"whisper-1","gpt-4o","gpt-3.5-turbo"}));
    }
}
