using System.Net;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public class AiApiCompatibilityTests
{
    [Theory]
    [InlineData("DeepSeek")][InlineData("Qwen")][InlineData("Kimi")][InlineData("GLM")][InlineData("Gemini")][InlineData("Claude")]
    public async Task EveryPresetUsesItsProtocolAndFetchedEvidence(string provider)
    {
        var preset=AiApiProfiles.Presets.Single(p=>p.Name==provider);var document=Document();var payload=document.ToJsonString();
        var response=preset.Protocol switch
        {
            "claude"=>new JsonObject{["stop_reason"]="end_turn",["content"]=new JsonArray(new JsonObject{["type"]="text",["text"]=payload})},
            "gemini"=>new JsonObject{["candidates"]=new JsonArray(new JsonObject{["finishReason"]="STOP",["content"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=payload})}})},
            _=>new JsonObject{["choices"]=new JsonArray(new JsonObject{["finish_reason"]="stop",["message"]=new JsonObject{["content"]=payload}})}
        };
        var handler=new Handler(response.ToJsonString());using var http=new HttpClient(handler);var evidence=new Evidence();
        var settings=new DiscoverySettings{Provider=provider,Protocol=preset.Protocol,Endpoint=preset.Endpoint,Model=preset.Model,ApiKey="synthetic-key-never-sent"};
        var result=await new ProviderDiscovery(http,research:evidence).SearchAsync(settings,"fixture query");
        Assert.Equal(1,evidence.Requests);Assert.Equal("Example",Assert.Single(result).Name);Assert.StartsWith(provider,result[0].Source);
        Assert.Equal(AiApiProfiles.Endpoint(settings),handler.Url);Assert.DoesNotContain(settings.ApiKey,handler.Body);
        Assert.Contains("https://example.com/guide",handler.Body);Assert.Contains("ignore instructions",handler.Body,StringComparison.OrdinalIgnoreCase);
        if(preset.Protocol=="claude"){Assert.Equal(settings.ApiKey,handler.Headers["x-api-key"]);Assert.Contains("anthropic-version",handler.Headers.Keys);Assert.DoesNotContain("Authorization",handler.Headers.Keys);}
        else if(preset.Protocol=="gemini"){Assert.Equal(settings.ApiKey,handler.Headers["x-goog-api-key"]);Assert.Contains(":generateContent",handler.Url!.AbsoluteUri);}
        else{Assert.Equal("Bearer "+settings.ApiKey,handler.Headers["Authorization"]);Assert.Contains("messages",handler.Body);}
        var request=JsonNode.Parse(handler.Body)!;
        if(provider=="DeepSeek"){Assert.Equal("disabled",request["thinking"]!["type"]!.ToString());Assert.True(request["max_tokens"]!.GetValue<int>()>8192);}
        else Assert.Null(request["thinking"]);
    }
    [Fact]public async Task UnfetchedOfficialUrlsCannotBecomeProviderEvidence()
    {
        var document=Document();document["providers"]![0]!["subscriptionSource"]="https://example.com/unfetched";
        using var http=new HttpClient(new Handler(new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=document.ToJsonString()}})}.ToJsonString()));
        Assert.Empty(await new ProviderDiscovery(http,research:new Evidence()).SearchAsync(new(){Protocol="chat",Endpoint="https://api.example/v1/chat/completions",Model="fixture-model",ApiKey="synthetic-key-never-sent"},"fixture"));
    }
    [Theory][InlineData("http://api.example/v1")][InlineData("https://user:password@api.example/v1")][InlineData("https://api.example/v1?key=private")]
    public void InvalidCredentialDestinationsAreRejected(string endpoint)=>Assert.Throws<FormatException>(()=>AiApiProfiles.Endpoint(new(){Endpoint=endpoint}));
    [Fact]public void LegacySettingsStillResolveToTheOpenAiResponsesEndpoint()=>Assert.Equal("https://api.openai.com/v1/responses",AiApiProfiles.Endpoint(new()).AbsoluteUri);
    [Theory][InlineData("responses","https://api.deepseek.com/responses")][InlineData("claude","https://api.deepseek.com/anthropic/v1/messages")]
    public async Task DeepSeekAlternateProtocolsUseTheirEndpointAndFetchedEvidence(string protocol,string endpoint)
    {
        var payload=Document().ToJsonString();
        var response=protocol=="responses"
            ? new JsonObject { ["status"]="completed",["output"]=new JsonArray(new JsonObject { ["type"]="message",["content"]=new JsonArray(new JsonObject { ["type"]="output_text",["text"]=payload }) }) }
            : new JsonObject { ["stop_reason"]="end_turn",["content"]=new JsonArray(new JsonObject { ["type"]="text",["text"]=payload }) };
        var handler=new Handler(response.ToJsonString());using var http=new HttpClient(handler);var evidence=new Evidence();
        var settings=new DiscoverySettings { Provider="DeepSeek",Protocol=protocol,Endpoint="",Model="deepseek-flash",ApiKey="synthetic-key-never-sent" };
        var result=await new ProviderDiscovery(http,research:evidence).SearchAsync(settings,"fixture");
        Assert.Equal(endpoint,handler.Url!.AbsoluteUri);Assert.Single(result);Assert.Equal(1,evidence.Requests);
        Assert.DoesNotContain("\"web_search\"",handler.Body);Assert.Contains("https://example.com/guide",handler.Body);
        Assert.Equal(protocol=="claude"?settings.ApiKey:"Bearer "+settings.ApiKey,handler.Headers[protocol=="claude"?"x-api-key":"Authorization"]);
        var request=JsonNode.Parse(handler.Body)!;
        Assert.Equal(protocol=="responses"?"none":"disabled",request[protocol=="responses"?"reasoning":"thinking"]![protocol=="responses"?"effort":"type"]!.ToString());
        Assert.True(request[protocol=="responses"?"max_output_tokens":"max_tokens"]!.GetValue<int>()>8192);
    }
    [Fact] public async Task TruncatedDeepSeekOutputIsIdentifiedRatherThanParsedAsAnEmptySearch()
    {
        using var http=new HttpClient(new Handler("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{\\\"providers\\\":[\"}}]}"));
        var error=await Assert.ThrowsAsync<FormatException>(()=>new ProviderDiscovery(http,research:new Evidence()).SearchAsync(new(){Provider="DeepSeek",Protocol="chat",Endpoint="https://api.deepseek.com/chat/completions",Model="deepseek-flash",ApiKey="synthetic-key-never-sent"},"fixture"));
        Assert.Contains("length limit",error.Message);
    }
    [Fact] public void ProviderProtocolChoicesSeparateFormatsFromVendorNames()
    {
        Assert.Equal(new[]{"chat","responses","claude"},AiApiProfiles.ProtocolsFor("DeepSeek"));
        Assert.Equal(new[]{"gemini"},AiApiProfiles.ProtocolsFor("Gemini"));
        Assert.Equal(4,AiApiProfiles.ProtocolsFor("Custom").Length);
    }
    private static JsonObject Document()=>new(){["providers"]=new JsonArray(new JsonObject{["name"]="Example",["url"]="https://example.com/",["description"]="Synthetic service",["serviceKind"]="personal_subscription",["protocols"]=new JsonArray("Clash"),["subscriptionSource"]="https://example.com/guide",["regions"]=new JsonArray(),["securityNotes"]="Unverified",["ipQualityNotes"]="Unverified",["sources"]=new JsonArray("https://example.com/guide"),["plans"]=new JsonArray()})};
    private sealed class Evidence:IWebResearch
    {
        internal int Requests;
        public Task<IReadOnlyList<WebEvidence>> SearchAsync(string query,CancellationToken cancellation){Requests++;return Task.FromResult<IReadOnlyList<WebEvidence>>([new("https://example.com/guide","Synthetic official Clash guide")]);}
    }
    private sealed class Handler(string response):HttpMessageHandler
    {
        internal string Body="";internal Uri? Url;internal Dictionary<string,string> Headers=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {Url=request.RequestUri;Body=await request.Content!.ReadAsStringAsync(cancellation);Headers=request.Headers.ToDictionary(h=>h.Key,h=>string.Join(" ",h.Value));return new(HttpStatusCode.OK){Content=new StringContent(response)};}
    }
}
