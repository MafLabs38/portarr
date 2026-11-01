using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Accounts;
using Session = Portarr.Http.Authentication.AuthenticationServiceCollectionExtensions;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The role × endpoint matrix for <b>every</b> route of the API (D-94): each one answers 401 without a session unless it
/// is on the short list of open endpoints, and each built-in role gets 403 exactly on the endpoints whose permission it
/// lacks. The list of open endpoints is written out here, so opening a new one is a decision a reviewer sees.
/// AccountsFixture keeps a hand-written matrix as the independent oracle for the permission mapping itself.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class EndpointSecurityFixture
{
    /// <summary>The endpoints that answer without a session: sign-in, first-run setup, and the public logo.</summary>
    private static readonly string[] OpenEndpoints =
    [
        "GET api/v1/auth/status",
        "POST api/v1/auth/setup",
        "POST api/v1/auth/login",
        "POST api/v1/auth/logout",
        "GET api/v1/portal/logo",
    ];

    private static readonly string[] Roles = ["Admin", "Operator", "Observer", "Guest"];

    private readonly TestDatabase _database;

    public EndpointSecurityFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private record struct Route(string Method, string Pattern, string[] Policies, bool Open);

    private static List<Route> Routes(IServiceProvider services)
    {
        return [.. services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/v1", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(method =>
                new Route(
                    method,
                    e.RoutePattern.RawText!,
                    [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().Distinct()],
                    e.Metadata.GetMetadata<IAllowAnonymous>() is not null)))
            .OrderBy(r => r.Pattern, StringComparer.Ordinal)
            .ThenBy(r => r.Method, StringComparer.Ordinal)];
    }

    // {id:int} -> 9999 (no such user or role: the built-in ones are protected), {section:regex(^(a|b)$)} -> a, any other parameter -> x.
    private static string Concrete(string pattern) => RouteParameter().Replace(pattern, m =>
    {
        if (m.Value.Contains(":int", StringComparison.Ordinal))
        {
            return "9999";
        }

        if (m.Value.Contains(":catalogSection", StringComparison.Ordinal))
        {
            return "apps";
        }

        return FirstAlternative().Match(m.Value) is { Success: true } alternative ? alternative.Groups[1].Value : "x";
    });

    [GeneratedRegex(@"\{(?:[^{}]|\{[^}]*\})+\}")]
    private static partial Regex RouteParameter();

    [GeneratedRegex(@"regex\(\^\(([^|)]+)")]
    private static partial Regex FirstAlternative();

    [Fact]
    public async Task should_open_only_the_endpoints_on_the_list_and_ask_every_other_one_to_sign_in()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        var routes = Routes(factory.Services);
        routes.Should().HaveCountGreaterThan(40, "the whole API is enumerated, not a sample");

        routes.Where(r => r.Open).Select(r => $"{r.Method} {r.Pattern}").Should().BeEquivalentTo(OpenEndpoints);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");
        foreach (var route in routes.Where(r => !r.Open))
        {
            using var request = Request(route);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{route.Method} {route.Pattern} must ask to sign in");
        }

        // Outside the API: the metrics ask to sign in too; the probes, the docs and the SPA fallback are open (or absent in a test host).
        (await client.GetAsync("/metrics", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "/metrics");
        foreach (var path in new[] { "/health/live", "/openapi/v1.json", "/docs/", "/apps" })
        {
            (await client.GetAsync(path, Token)).StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NotFound], path);
        }
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Operator")]
    [InlineData("Observer")]
    [InlineData("Guest")]
    public async Task should_refuse_the_role_exactly_the_endpoints_whose_permission_it_lacks(string role)
    {
        // Catalog writes are off by default and answer 403 to everyone; turned on, only the permission decides.
        // One host per role: the write rate limit (30 a minute per client) is counted before the permission is checked.
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync(), new Dictionary<string, string?> { ["Catalog:Writable"] = "true" });
        using var client = await factory.CreateSignedInClientAsync(role);

        var failures = new List<string>();
        foreach (var route in Routes(factory.Services).Where(r => !r.Open))
        {
            var granted = Allowed(role, route.Policies);
            using var request = Request(route);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);
            var refused = response.StatusCode == HttpStatusCode.Forbidden;
            if (refused == granted || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests)
            {
                failures.Add($"{role} {route.Method} {route.Pattern} -> {(int)response.StatusCode}, expected {(granted ? "allowed" : "403")}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task should_name_a_known_permission_or_the_session_on_every_protected_endpoint()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());

        foreach (var route in Routes(factory.Services).Where(r => !r.Open))
        {
            foreach (var policy in route.Policies)
            {
                (Permissions.Keys.Contains(policy) || policy == Session.SessionPolicyName)
                    .Should().BeTrue($"{route.Method} {route.Pattern} names the policy '{policy}'");
            }
        }
    }

    // Every permission policy needs its permission; the session policy and an endpoint without one only need a sign-in (the client is a cookie session).
    private static bool Allowed(string role, string[] policies)
    {
        return policies.All(policy => policy == Session.SessionPolicyName || BuiltInRoles.Find(role)!.Permissions.Contains(policy));
    }

    private static HttpRequestMessage Request(Route route)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), "/" + Concrete(route.Pattern));
        if (route.Method is "POST" or "PUT" or "PATCH")
        {
            // Image uploads are multipart: another media type is answered 415 before the permission is looked at.
            request.Content = route.Pattern.EndsWith("/logo", StringComparison.Ordinal) || route.Pattern.EndsWith("/avatar", StringComparison.Ordinal)
                ? new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "image.png" } }
                : JsonContent.Create(new { });
        }

        return request;
    }
}
