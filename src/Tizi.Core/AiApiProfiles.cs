namespace Tizi.Core;

public sealed record AiApiPreset(string Name, string Protocol, string Endpoint, string Model);
public static class AiApiProfiles
{
    public static readonly AiApiPreset[] Presets =
    [
        new("OpenAI", "responses", "https://api.openai.com/v1/responses", "gpt-5.4-mini"),
        new("DeepSeek", "chat", "https://api.deepseek.com/chat/completions", "deepseek-flash"),
        new("Qwen", "chat", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "qwen-plus"),
        new("Kimi", "chat", "https://api.moonshot.cn/v1/chat/completions", "moonshot-v1-32k"),
        new("GLM", "chat", "https://open.bigmodel.cn/api/paas/v4/chat/completions", "glm-4-flash"),
        new("Gemini", "gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash"),
        new("Claude", "claude", "https://api.anthropic.com/v1/messages", "claude-sonnet-4-5"),
        new("Custom", "chat", "", "")
    ];
    public static string[] ProtocolsFor(string provider) => provider switch
    {
        "OpenAI" => ["responses","chat"], "DeepSeek" => ["chat","responses","claude"],
        "Qwen" or "Kimi" or "GLM" => ["chat"], "Gemini" => ["gemini"], "Claude" => ["claude"],
        _ => ["responses","chat","claude","gemini"]
    };
    public static string PresetEndpoint(string provider,string protocol) => (provider,protocol) switch
    {
        ("OpenAI","chat") => "https://api.openai.com/v1/chat/completions",
        ("DeepSeek","responses") => "https://api.deepseek.com/responses",
        ("DeepSeek","claude") => "https://api.deepseek.com/anthropic/v1/messages",
        _ => Presets.FirstOrDefault(p=>p.Name==provider&&p.Protocol==protocol)?.Endpoint??""
    };
    public static Uri Endpoint(DiscoverySettings settings)
    {
        if (settings.Protocol is not ("responses" or "chat" or "claude" or "gemini")) throw new FormatException("Unknown AI API protocol.");
        var endpoint = settings.Endpoint.Trim();
        if (endpoint.Length == 0) endpoint = PresetEndpoint(settings.Provider,settings.Protocol);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" && !(uri.Scheme=="http" && uri.IsLoopback)
            || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0) throw new FormatException("Enter a valid HTTPS API endpoint without credentials in the URL.");
        if (settings.Protocol=="gemini" && !uri.AbsolutePath.EndsWith(":generateContent",StringComparison.Ordinal))
            uri=new Uri(uri.AbsoluteUri.TrimEnd('/')+"/models/"+Uri.EscapeDataString(settings.Model.Trim().Replace("models/",""))+":generateContent");
        return uri;
    }
}
