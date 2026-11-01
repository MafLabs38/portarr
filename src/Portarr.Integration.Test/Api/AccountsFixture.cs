using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Portarr.Api.V1.Accounts;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// Local accounts and roles (V7, D-92 to D-97): the first-run race, sign-in and lockout, CSRF, the role × endpoint
/// matrix, per-user API keys and their scope, and the rules that keep an Admin (last Admin, own account). Pure rules are
/// unit-tested (AccountRulesFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class AccountsFixture
{
    private readonly TestDatabase _database;

    public AccountsFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_accept_only_one_setup_whatever_the_race()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            client.PostAsJsonAsync("/api/v1/auth/setup", new { username = $"admin{i}", password = PortarrFactory.Password }, Token)));
        var later = await client.PostAsJsonAsync("/api/v1/auth/setup", new { username = "late", password = PortarrFactory.Password }, Token);

        attempts.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        attempts.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
        later.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/status", Token)).GetProperty("setupRequired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_401_without_a_session_and_allow_only_the_public_endpoints()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/api/v1/apps", "/api/v1/system/status", "/api/v1/auth/me", "/api/v1/infrastructure/stream", "/metrics" })
        {
            (await client.GetAsync(path, Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }

        foreach (var path in new[] { "/api/v1/auth/status", "/health/live", "/openapi/v1.json", "/docs/", "/apps" })
        {
            (await client.GetAsync(path, Token)).StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NotFound], path);
        }
    }

    [Fact]
    public async Task should_fail_sign_in_with_one_generic_message_and_lock_the_account()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var admin = await factory.CreateSignedInClientAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");

        var unknown = await LoginAsync(client, "nobody", "wrong password here");
        var wrong = await LoginAsync(client, PortarrFactory.AdminUser, "wrong password here");
        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(client, PortarrFactory.AdminUser, "wrong password here");
        }

        var lockedGood = await LoginAsync(client, PortarrFactory.AdminUser, PortarrFactory.Password);

        async Task<string?> DetailAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("detail").GetString();
        (await DetailAsync(unknown)).Should().Be(await DetailAsync(wrong));
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        lockedGood.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "five failures lock the account, even for the right password");

        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        (await LoginAsync(client, PortarrFactory.AdminUser, PortarrFactory.Password)).StatusCode.Should().Be(HttpStatusCode.OK);

        var activity = await admin.GetStringAsync("/api/v1/users/activity", Token);
        activity.Should().Contain("login.failure").And.Contain("wrong password; locked for 5 minutes").And.NotContain("wrong password here");
    }

    [Fact]
    public async Task should_require_the_csrf_header_for_cookie_writes_but_not_for_keys()
    {
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        using var withoutHeader = await factory.CreateSignedInClientAsync(csrfHeader: false);
        using var withHeader = await factory.CreateSignedInClientAsync();

        var refused = await withoutHeader.PutAsJsonAsync("/api/v1/auth/profile", new { displayName = "Root" }, Token);
        var accepted = await withHeader.PutAsJsonAsync("/api/v1/auth/profile", new { displayName = "Root" }, Token);
        var read = await withoutHeader.GetAsync("/api/v1/auth/me", Token);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await withHeader.PostAsJsonAsync("/api/v1/auth/keys", new { name = "ci", scope = "full" }, Token);
        var key = (await created.Content.ReadFromJsonAsync<CreatedApiKeyResource>(Token))!.Value;
        using var keyClient = factory.CreateClient();
        keyClient.DefaultRequestHeaders.Add("X-Api-Key", key);
        (await keyClient.PostAsJsonAsync("/api/v1/maintenance/refresh", new { }, Token)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // Each row: role, path, whether the role may call it. One endpoint per permission.
    public static TheoryData<string, string, bool> Matrix()
    {
        var data = new TheoryData<string, string, bool>();
        var rows = new (string Path, string[] Roles)[]
        {
            ("GET /api/v1/apps", ["Admin", "Operator", "Observer", "Guest"]),
            ("GET /api/v1/environments", ["Admin", "Operator", "Observer"]),
            ("GET /api/v1/data", ["Admin", "Operator", "Observer"]),
            ("GET /api/v1/maintenance", ["Admin", "Operator", "Observer"]),
            ("GET /api/v1/assistant", ["Admin", "Operator", "Observer"]),
            ("GET /api/v1/assistant/chat", ["Admin", "Operator"]),
            ("GET /api/v1/catalog/apps/none", ["Admin", "Operator"]),
            ("GET /api/v1/system/status", ["Admin", "Operator", "Observer"]),
            ("PUT /api/v1/system/logging", ["Admin"]),
            ("GET /api/v1/users", ["Admin"]),
            ("GET /api/v1/roles", ["Admin"]),
            ("GET /api/v1/portal", ["Admin", "Operator", "Observer", "Guest"]),
        };
        foreach (var (path, allowed) in rows)
        {
            foreach (var role in new[] { "Admin", "Operator", "Observer", "Guest" })
            {
                data.Add(role, path, allowed.Contains(role));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task should_give_each_role_exactly_its_permissions(string role, string endpoint, bool allowed)
    {
        await using var factory = await FactoryAsync();
        using var client = await factory.CreateSignedInClientAsync(role);
        var (method, path) = (endpoint.Split(' ')[0], endpoint.Split(' ')[1]);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { level = "Information" });
        }

        var response = await client.SendAsync(request, Token);

        if (allowed)
        {
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task should_scope_api_keys_and_never_exceed_the_owners_role()
    {
        await using var factory = await FactoryAsync();
        using var admin = await factory.CreateSignedInClientAsync();
        using var observer = await factory.CreateSignedInClientAsync("Observer");

        var read = await CreateKeyAsync(admin, "read");
        var full = await CreateKeyAsync(admin, "full");
        var observerFull = await CreateKeyAsync(observer, "full");
        using var anonymous = factory.CreateClient();

        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", read)).Should().Be(HttpStatusCode.OK);
        (await Send(anonymous, HttpMethod.Get, "/api/v1/users", read)).Should().Be(HttpStatusCode.Forbidden);
        (await Send(anonymous, HttpMethod.Put, "/api/v1/system/logging", read)).Should().Be(HttpStatusCode.Forbidden, "a read-only key cannot write");
        (await Send(anonymous, HttpMethod.Put, "/api/v1/system/logging", full)).Should().NotBe(HttpStatusCode.Forbidden, "a full key of an Admin can write");
        (await Send(anonymous, HttpMethod.Get, "/api/v1/users", full)).Should().Be(HttpStatusCode.Forbidden, "users and roles are managed from a session, never with a key");
        (await Send(anonymous, HttpMethod.Get, "/api/v1/users", observerFull)).Should().Be(HttpStatusCode.Forbidden, "a full key of an Observer is still an Observer");
        (await Send(anonymous, HttpMethod.Post, "/api/v1/auth/keys", full)).Should().Be(HttpStatusCode.Forbidden, "keys are managed from a session");
        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", "ptr_unknown")).Should().Be(HttpStatusCode.Unauthorized);

        var listed = await admin.GetStringAsync("/api/v1/auth/keys", Token);
        listed.Should().NotContain(read).And.NotContain(full);

        var observerUser = (await admin.GetFromJsonAsync<List<UserResource>>("/api/v1/users", Token))!.Single(u => u.Username == "observer-user");
        (await admin.PutAsJsonAsync($"/api/v1/users/{observerUser.Id}", new { disabled = true }, Token)).EnsureSuccessStatusCode();
        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", observerFull)).Should().Be(HttpStatusCode.Unauthorized, "a disabled user's keys stop working");
    }

    [Fact]
    public async Task should_let_an_admin_list_and_revoke_the_keys_of_a_user_and_revoke_them_all_on_a_password_reset()
    {
        await using var factory = await FactoryAsync();
        using var admin = await factory.CreateSignedInClientAsync();
        using var observer = await factory.CreateSignedInClientAsync("Observer");
        using var anonymous = factory.CreateClient();
        var observerId = (await admin.GetFromJsonAsync<List<UserResource>>("/api/v1/users", Token))!.Single(u => u.Username == "observer-user").Id;
        var first = await CreateKeyAsync(observer, "read");
        var second = await CreateKeyAsync(observer, "full");

        var listed = (await admin.GetFromJsonAsync<List<ApiKeyResource>>($"/api/v1/users/{observerId}/keys", Token))!;
        listed.Should().HaveCount(2).And.OnlyContain(k => k.Active);
        (await admin.DeleteAsync($"/api/v1/users/{observerId}/keys/{listed.Single(k => k.Scope == "read").Id}", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", first)).Should().Be(HttpStatusCode.Unauthorized, "an admin revoked it");
        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", second)).Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/api/v1/users/{observerId}/keys/9999", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/v1/users/9999/keys", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await admin.PostAsJsonAsync($"/api/v1/users/{observerId}/password", new { password = "a brand new password" }, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", second)).Should().Be(HttpStatusCode.Unauthorized, "a password reset revokes the keys");
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("Mcp:ApiKey")]
    public async Task should_give_an_environment_key_the_view_permissions_and_never_the_administration(string setting)
    {
        var key = new string('k', 48);
        await using var factory = new PortarrFactory(await _database.CreateOptionsAsync(), new Dictionary<string, string?> { [setting] = key, ["Mcp:Enabled"] = "true" });
        using var anonymous = factory.CreateClient();

        (await Send(anonymous, HttpMethod.Get, "/api/v1/apps", key)).Should().Be(HttpStatusCode.OK);
        (await Send(anonymous, HttpMethod.Get, "/api/v1/users", key)).Should().Be(HttpStatusCode.Forbidden, "the system key is not an Admin");
        (await Send(anonymous, HttpMethod.Put, "/api/v1/system/logging", key)).Should().Be(HttpStatusCode.Forbidden);
        (await Send(anonymous, HttpMethod.Get, "/api/v1/roles", key)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_keep_an_admin_and_protect_ones_own_account()
    {
        await using var factory = await FactoryAsync();
        using var admin = await factory.CreateSignedInClientAsync();
        var users = await admin.GetFromJsonAsync<List<UserResource>>("/api/v1/users", Token);
        var roles = (await admin.GetFromJsonAsync<RoleListResource>("/api/v1/roles", Token))!;
        var adminRole = roles.Roles.Single(r => r.Name == "Admin").Id;
        var me = users!.Single(u => u.Username == "admin");

        (await admin.PutAsJsonAsync($"/api/v1/users/{me.Id}", new { roleId = roles.Roles.Single(r => r.Name == "Guest").Id }, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/v1/users/{me.Id}", new { disabled = true }, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/v1/users/{me.Id}", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // users.manage can make an Admin, so only Admin holds it: a custom role cannot be given it.
        var managers = await admin.PostAsJsonAsync("/api/v1/roles", new { name = "Managers", permissions = new[] { "users.manage", "apps.view" } }, Token);
        managers.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CreateUserAsync(admin, "second-admin", adminRole)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_manage_custom_roles_and_keep_the_built_in_ones()
    {
        await using var factory = await FactoryAsync();
        using var admin = await factory.CreateSignedInClientAsync();
        var roles = (await admin.GetFromJsonAsync<RoleListResource>("/api/v1/roles", Token))!;
        var builtIn = roles.Roles.Where(r => r.BuiltIn).Select(r => r.Name).ToList();

        var created = await admin.PostAsJsonAsync("/api/v1/roles", new { name = "Viewer", permissions = new[] { "apps.view" } }, Token);
        var role = (await created.Content.ReadFromJsonAsync<RoleResource>(Token))!;
        var renamed = await admin.PutAsJsonAsync($"/api/v1/roles/{role.Id}", new { name = "Viewer plus", permissions = new[] { "apps.view", "data.view" } }, Token);
        await CreateUserAsync(admin, "viewer", role.Id);

        builtIn.Should().BeEquivalentTo("Admin", "Operator", "Observer", "Guest");
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/api/v1/roles/{role.Id}", Token)).StatusCode.Should().Be(HttpStatusCode.Conflict, "a user still has it");
        (await admin.DeleteAsync($"/api/v1/roles/{roles.Roles.Single(r => r.Name == "Guest").Id}", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/v1/roles/{roles.Roles.Single(r => r.Name == "Admin").Id}", new { name = "Admin", permissions = new[] { "apps.view" } }, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/v1/roles", new { name = "Bad", permissions = new[] { "secrets.view" } }, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/roles", new { name = "admin", permissions = new[] { "apps.view" } }, Token)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var viewer = await factory.CreateSignedInClientAsync("Viewer plus");
        (await viewer.GetAsync("/api/v1/data", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/v1/maintenance", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_change_the_password_with_the_current_one_and_end_other_sessions()
    {
        await using var factory = await FactoryAsync();
        using var admin = await factory.CreateSignedInClientAsync();
        using var other = await factory.CreateSignedInClientAsync("Operator");
        const string newPassword = "an entirely new passphrase";

        var wrong = await other.PutAsJsonAsync("/api/v1/auth/password", new { currentPassword = "not the password", newPassword }, Token);
        var changed = await other.PutAsJsonAsync("/api/v1/auth/password", new { currentPassword = PortarrFactory.Password, newPassword }, Token);

        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Clock.Advance(TimeSpan.FromSeconds(11));
        using var old = await factory.CreateSignedInClientAsync("Operator");
        (await old.GetAsync("/api/v1/auth/me", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old session ends with the password it was opened with");
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Requested-With", "portarr");
        (await LoginAsync(anonymous, "operator-user", newPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_store_an_avatar_by_content_and_refuse_an_svg()
    {
        await using var factory = await FactoryAsync();
        using var admin = await factory.CreateSignedInClientAsync();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        var svg = await UploadAsync(admin, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "a.svg", "image/svg+xml");
        var disguised = await UploadAsync(admin, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "a.png", "image/png");
        var ok = await UploadAsync(admin, png, "me.png", "image/png");
        var me = (await ok.Content.ReadFromJsonAsync<MeResource>(Token))!;
        var served = await admin.GetAsync(me.AvatarUrl, Token);

        svg.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        disguised.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        me.AvatarUrl.Should().MatchRegex("^/api/v1/avatars/\\d+\\?v=[0-9a-f]{16}$");
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        served.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task should_guard_the_first_run_with_the_one_time_setup_code()
    {
        var options = await _database.CreateOptionsAsync();
        await using var factory = new PortarrFactory(options, new Dictionary<string, string?> { ["Auth:RequireSetupCode"] = "true" });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");
        var codeFile = Path.Combine(options.DataFolder, "setup-code");
        var code = (await File.ReadAllTextAsync(codeFile, Token)).Trim();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/status", Token);
        var missing = await client.PostAsJsonAsync("/api/v1/auth/setup", new { username = "admin", password = PortarrFactory.Password }, Token);
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/setup", new { username = "admin", password = PortarrFactory.Password, setupCode = "0000-0000-0000-0000" }, Token);
        var created = await client.PostAsJsonAsync("/api/v1/auth/setup", new { username = "admin", password = PortarrFactory.Password, setupCode = code }, Token);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/status", Token);

        status.GetProperty("setupCodeRequired").GetBoolean().Should().BeTrue();
        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        after.GetProperty("setupCodeRequired").GetBoolean().Should().BeFalse();
        File.Exists(codeFile).Should().BeFalse("the code is gone once the administrator exists");
    }

    [Fact]
    public async Task should_issue_a_secure_host_prefixed_cookie_when_asked_to()
    {
        var options = await _database.CreateOptionsAsync();
        await using var factory = new PortarrFactory(options, new Dictionary<string, string?> { ["Auth:CookieSecurity"] = "Always" });
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");

        var response = await client.PostAsJsonAsync("/api/v1/auth/setup", new { username = PortarrFactory.AdminUser, password = PortarrFactory.Password }, Token);

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().StartWith("__Host-portarr_session=").And.Contain("secure", Exactly.Once()).And.Contain("httponly").And.Contain("samesite=strict");
        Directory.Exists(Path.Combine(options.DataFolder, "keys")).Should().BeTrue("the cookie keys live in the data folder");
    }

    [Fact]
    public async Task should_require_the_csrf_header_on_the_mcp_endpoint_for_a_cookie_session()
    {
        await using var factory = await FactoryAsync();
        using var withoutHeader = await factory.CreateSignedInClientAsync(csrfHeader: false);

        var response = await withoutHeader.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" }, Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_create_the_first_admin_from_a_password_file()
    {
        var options = await _database.CreateOptionsAsync();
        var file = Path.Combine(options.DataFolder, "admin-password");
        await File.WriteAllTextAsync(file, PortarrFactory.Password + "\n", Token);
        await using var factory = new PortarrFactory(options, new Dictionary<string, string?> { ["Auth:AdminUser"] = "root", ["Auth:AdminPasswordFile"] = file });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "portarr");

        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/status", Token);

        status.GetProperty("setupRequired").GetBoolean().Should().BeFalse();
        (await LoginAsync(client, "root", PortarrFactory.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<PortarrFactory> FactoryAsync()
    {
        var factory = new PortarrFactory(await _database.CreateOptionsAsync());
        _ = factory.Services;
        return factory;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { username, password }, Token);

    private static async Task<string> CreateKeyAsync(HttpClient session, string scope)
    {
        var response = await session.PostAsJsonAsync("/api/v1/auth/keys", new { name = $"{scope} key", scope }, Token);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyResource>(Token))!.Value;
    }

    private static async Task<HttpStatusCode> Send(HttpClient client, HttpMethod method, string path, string key)
    {
        using var request = new HttpRequestMessage(method, path) { Content = method == HttpMethod.Get ? null : JsonContent.Create(new { level = "Information", name = "x", scope = "read" }) };
        request.Headers.Add("X-Api-Key", key);
        return (await client.SendAsync(request, Token)).StatusCode;
    }

    private static async Task<UserResource> CreateUserAsync(HttpClient admin, string username, int roleId)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/users", new { username, password = PortarrFactory.Password, roleId }, Token);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<UserResource>(Token))!;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return client.PutAsync("/api/v1/auth/avatar", form, Token);
    }
}
