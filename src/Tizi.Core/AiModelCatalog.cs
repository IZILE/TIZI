using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Tizi.Core;

public sealed class AiModelCatalog(HttpClient http)
{
    public static Uri Endpoint(DiscoverySettings settings)
    {
        var uri = AiApiProfiles.Endpoint(settings);
        if (uri.Host == "api.deepseek.com") return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/models");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (settings.Protocol == "gemini")
        {
            var at = path.IndexOf("/models/", StringComparison.Ordinal);
            if (at >= 0) path = path[..at];
        }
        else foreach (var suffix in new[] { "/chat/completions", "/responses", "/messages" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { path = path[..^suffix.Length]; break; }
        return new UriBuilder(uri) { Path = path + "/models", Query = "", Fragment = "" }.Uri;
    }
    public async Task<List<string>> ListAsync(DiscoverySettings settings, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new FormatException("Enter an API key before detecting models.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var endpoint = Endpoint(settings); var models = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>();
        string? cursor = null;
        for (var page = 0; page < 5; page++)
        {
            var address = new UriBuilder(endpoint);
            if (cursor is not null) address.Query = (settings.Protocol == "gemini" ? "pageToken=" : "after_id=") + Uri.EscapeDataString(cursor);
            using var request = new HttpRequestMessage(HttpMethod.Get, address.Uri);
            if (settings.Protocol == "claude" && endpoint.Host != "api.deepseek.com") { request.Headers.Add("x-api-key", settings.ApiKey.Trim()); request.Headers.Add("anthropic-version", "2023-06-01"); }
            else if (settings.Protocol == "gemini") request.Headers.Add("x-goog-api-key", settings.ApiKey.Trim());
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Model list HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            { if (bytes.Length + read > 1024 * 1024) throw new FormatException("Model list is too large."); bytes.Write(buffer, 0, read); }
            var root = JsonNode.Parse(bytes.ToArray(), documentOptions: new System.Text.Json.JsonDocumentOptions { MaxDepth = 12 }) as JsonObject ?? throw new FormatException("Invalid model list.");
            var entries = (settings.Protocol == "gemini" ? root["models"] : root["data"]) as JsonArray ?? throw new FormatException("Invalid model list.");
            foreach (var item in entries.OfType<JsonObject>())
            {
                if (settings.Protocol == "gemini" && item["supportedGenerationMethods"] is JsonArray methods && !methods.Any(m => m?.ToString() == "generateContent")) continue;
                var id = item[settings.Protocol == "gemini" ? "name" : "id"]?.ToString().Trim();
                if (id?.StartsWith("models/", StringComparison.Ordinal) == true) id = id[7..];
                if (id is { Length: > 0 and <= 80 } && !id.Any(char.IsControl)) models.Add(id);
                if (models.Count > 500) throw new FormatException("Model list is too large.");
            }
            cursor = settings.Protocol == "gemini" ? root["nextPageToken"]?.ToString() : root["has_more"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true ? root["last_id"]?.ToString() : null;
            if (string.IsNullOrEmpty(cursor)) break;
            if (cursor.Length > 2048 || !cursors.Add(cursor) || page == 4) throw new FormatException("Model list pagination is incomplete.");
        }
        if (models.Count == 0) throw new FormatException("No models returned. You can enter a model manually.");
        return models.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
