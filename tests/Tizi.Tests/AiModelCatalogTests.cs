using System.Net;
using System.Text.Json.Nodes;
using Tizi.Core;

namespace Tizi.Tests;

public sealed class AiModelCatalogTests
{
    [Theory]
    [InlineData("OpenAI","responses","https://api.openai.com/v1/responses","https://api.openai.com/v1/models")]
    [InlineData("DeepSeek","chat","https://api.deepseek.com/chat/completions","https://api.deepseek.com/models")]
    [InlineData("DeepSeek","claude","https://api.deepseek.com/anthropic/v1/messages","https://api.deepseek.com/models")]
    [InlineData("Qwen","chat","https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions","https://dashscope.aliyuncs.com/compatible-mode/v1/models")]
    [InlineData("Kimi","chat","https://api.moonshot.cn/v1/chat/completions","https://api.moonshot.cn/v1/models")]
    [InlineData("GLM","chat","https://open.bigmodel.cn/api/paas/v4/chat/completions","https://open.bigmodel.cn/api/paas/v4/models")]
    [InlineData("Claude","claude","https://api.anthropic.com/v1/messages","https://api.anthropic.com/v1/models")]
    [InlineData("Gemini","gemini","https://generativelanguage.googleapis.com/v1beta","https://generativelanguage.googleapis.com/v1beta/models")]
    [InlineData("Custom","chat","https://gateway.example.test/prefix/v1/chat/completions","https://gateway.example.test/prefix/v1/models")]
    public void CatalogUsesConfiguredOriginAndApiPrefix(string provider,string protocol,string endpoint,string expected) => Assert.Equal(expected,AiModelCatalog.Endpoint(new(){Provider=provider,Protocol=protocol,Endpoint=endpoint}).AbsoluteUri);

    [Fact] public async Task BearerCatalogDeduplicatesModelsAndRejectsUnsafeNames()
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"deepseek-flash\"},{\"id\":\"deepseek-flash\"},{\"id\":\"deepseek-v4-pro\"},{\"id\":\"bad\\nname\"}]}")});
        using var http=new HttpClient(handler);var result=await new AiModelCatalog(http).ListAsync(new(){Provider="DeepSeek",Protocol="chat",Endpoint="https://api.deepseek.com/chat/completions",ApiKey="synthetic-key-never-sent"});
        Assert.Equal(new[]{"deepseek-flash","deepseek-v4-pro"},result);Assert.Equal("Bearer synthetic-key-never-sent",handler.Requests[0].Headers.Authorization?.ToString());Assert.DoesNotContain("synthetic",handler.Requests[0].RequestUri!.AbsoluteUri);
    }
    [Fact] public async Task GeminiFiltersGenerationModelsAndFollowsBoundedPageTokens()
    {
        var handler=new Handler(request=>new(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.Query.Length==0?"{\"models\":[{\"name\":\"models/gemini-chat\",\"supportedGenerationMethods\":[\"generateContent\"]},{\"name\":\"models/embedding\",\"supportedGenerationMethods\":[\"embedContent\"]}],\"nextPageToken\":\"page+two\"}":"{\"models\":[{\"name\":\"models/gemini-other\",\"supportedGenerationMethods\":[\"generateContent\"]}]}")});
        using var http=new HttpClient(handler);var result=await new AiModelCatalog(http).ListAsync(new(){Protocol="gemini",Endpoint="https://generativelanguage.googleapis.com/v1beta",ApiKey="synthetic-key-never-sent"});
        Assert.Equal(new[]{"gemini-chat","gemini-other"},result);Assert.Equal(2,handler.Requests.Count);Assert.Contains("pageToken=page%2Btwo",handler.Requests[1].RequestUri!.AbsoluteUri);Assert.True(handler.Requests.All(r=>r.Headers.Contains("x-goog-api-key")&&!r.Headers.Contains("Authorization")));
    }
    [Fact] public async Task AnthropicUsesItsAuthAndCursorWithoutMovingToAnotherOrigin()
    {
        var handler=new Handler(request=>new(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.Query.Length==0?"{\"data\":[{\"id\":\"claude-one\"}],\"has_more\":true,\"last_id\":\"cursor\"}":"{\"data\":[{\"id\":\"claude-two\"}],\"has_more\":false}")});
        using var http=new HttpClient(handler);Assert.Equal(2,(await new AiModelCatalog(http).ListAsync(new(){Protocol="claude",Endpoint="https://api.anthropic.com/v1/messages",ApiKey="synthetic-key-never-sent"})).Count);
        Assert.True(handler.Requests.All(r=>r.Headers.Contains("x-api-key")&&r.Headers.Contains("anthropic-version")&&!r.Headers.Contains("Authorization")));Assert.Equal("?after_id=cursor",handler.Requests[1].RequestUri!.Query);
    }
    [Fact] public async Task ErrorAndEmptyCatalogDoNotInventAvailability()
    {
        using var denied=new HttpClient(new Handler(_=>new(HttpStatusCode.Unauthorized)));
        var error=await Assert.ThrowsAsync<HttpRequestException>(()=>new AiModelCatalog(denied).ListAsync(new(){ApiKey="synthetic-key-never-sent"}));Assert.Equal(HttpStatusCode.Unauthorized,error.StatusCode);
        using var empty=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[]}")}));
        await Assert.ThrowsAsync<FormatException>(()=>new AiModelCatalog(empty).ListAsync(new(){ApiKey="synthetic-key-never-sent"}));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests.Add(request);return Task.FromResult(reply(request));}
    }
}
