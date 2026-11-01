using System.Net;
using System.Text;
using System.Text.Json;

namespace Portarr.Integration.Test.Api;

/// <summary>A JSON-RPC client for the MCP endpoint over Streamable HTTP (stateless), shared by the MCP and catalog fixtures.</summary>
internal static class McpTestClient
{
    public const string Initialize = """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""";

    /// <summary>One JSON-RPC request, with the key as <c>X-Api-Key</c> when there is one.</summary>
    public static HttpRequestMessage Request(string method, string parameters, string? key, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent($$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{parameters}}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        if (origin is not null)
        {
            request.Headers.Add("X-Portarr-Origin", origin);
        }

        return request;
    }

    /// <summary>The whole JSON-RPC answer (<c>result</c> or <c>error</c>), sent as JSON or as one server-sent event.</summary>
    public static async Task<JsonElement> SendAsync(HttpClient client, string method, string parameters, string? key, string? origin = null)
    {
        using var request = Request(method, parameters, key, origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal))?["data: ".Length..] ?? body;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public static Task<JsonElement> CallRawAsync(HttpClient client, string tool, string arguments, string? key, string? origin = null) =>
        SendAsync(client, "tools/call", $$"""{"name":"{{tool}}","arguments":{{arguments}}}""", key, origin);

    /// <summary>The first text of a tool answer; fails the test when the tool reported an error.</summary>
    public static async Task<string> CallAsync(HttpClient client, string tool, string arguments, string? key)
    {
        var response = await CallRawAsync(client, tool, arguments, key);
        var text = TextOf(response);
        IsError(response).Should().BeFalse($"{tool}: {text}");
        return text;
    }

    public static string TextOf(JsonElement response) => response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;

    public static bool IsError(JsonElement response) => response.GetProperty("result").TryGetProperty("isError", out var isError) && isError.GetBoolean();

    public static async Task<IReadOnlyList<string>> ToolNamesAsync(HttpClient client, string? key)
    {
        var response = await SendAsync(client, "tools/list", "{}", key);
        return response.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
    }
}
