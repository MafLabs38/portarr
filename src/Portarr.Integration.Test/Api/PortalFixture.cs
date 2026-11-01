using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The portal's identity over HTTP (D-91): the logo upload, its serving with the headers that keep an image from being
/// a page, the read-only refusal, and the operation each identity edit is audited under. The logic is unit-tested
/// (PortalServiceFixture, PortalLogoFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PortalFixture : IClassFixture<CatalogEditHosts>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly CatalogEditHosts _hosts;

    public PortalFixture(CatalogEditHosts hosts)
    {
        _hosts = hosts;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MultipartFormDataContent File(byte[] bytes, string name, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task should_serve_nothing_until_a_logo_is_uploaded_then_serve_it_as_an_image_that_cannot_be_a_page()
    {
        await _hosts.ResetWritableAsync();
        using var client = await _hosts.Writable.CreateSignedInClientAsync();
        using var anonymous = _hosts.Writable.CreateClient();
        (await anonymous.GetAsync("/api/v1/portal/logo", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var upload = await client.PutAsync("/api/v1/portal/logo", File(Png, "My Logo.png", "image/png"), Token);
        var portal = await client.GetFromJsonAsync<JsonElement>("/api/v1/portal", Token);
        var url = portal.GetProperty("logoUrl").GetString()!;
        using var served = await anonymous.GetAsync(url, Token);
        using var stale = await anonymous.GetAsync("/api/v1/portal/logo?v=0000", Token);

        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        url.Should().MatchRegex(@"^/api/v1/portal/logo\?v=[0-9a-f]{12}$");
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await served.Content.ReadAsByteArrayAsync(Token)).Should().Equal(Png);
        served.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        served.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'").And.Contain("sandbox");
        served.Headers.CacheControl!.ToString().Should().Contain("immutable", "the hash in the URL names these bytes");
        served.Headers.ETag.Should().NotBeNull();
        stale.Headers.CacheControl!.ToString().Should().Contain("max-age=300").And.NotContain("immutable", "a hash that does not match caches briefly");

        (await client.DeleteAsync("/api/v1/portal/logo", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/v1/portal/logo", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_refuse_what_is_not_an_image_of_an_accepted_kind()
    {
        await _hosts.ResetWritableAsync();
        using var client = await _hosts.Writable.CreateSignedInClientAsync();
        var scripted = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();
        var oversized = Png.Concat(new byte[1024 * 1024]).ToArray();

        var notAnImage = await client.PutAsync("/api/v1/portal/logo", File("plain text"u8.ToArray(), "logo.png", "image/png"), Token);
        var script = await client.PutAsync("/api/v1/portal/logo", File(scripted, "logo.svg", "image/svg+xml"), Token);
        var wrongExtension = await client.PutAsync("/api/v1/portal/logo", File(Png, "logo.gif", "image/gif"), Token);
        var tooBig = await client.PutAsync("/api/v1/portal/logo", File(oversized, "logo.png", "image/png"), Token);
        var empty = await client.PutAsync("/api/v1/portal/logo", File([], "logo.png", "image/png"), Token);

        foreach (var response in new[] { notAnImage, script, wrongExtension, empty })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        tooBig.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
        Directory.Exists(Path.Combine(_hosts.Folder, "branding")).Should().BeFalse("nothing was stored");
    }

    [Fact]
    public async Task should_refuse_an_upload_when_catalog_writes_are_off()
    {
        using var client = await _hosts.ReadOnly.CreateSignedInClientAsync();

        var response = await client.PutAsync("/api/v1/portal/logo", File(Png, "logo.png", "image/png"), Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("""{"name":"My lab"}""", "set-name")]
    [InlineData("""{"colors":{"name":"#112233"}}""", "set-colors")]
    [InlineData("""{"name":"My lab","colors":{"version":"accent"}}""", "set-identity")]
    public async Task should_audit_an_identity_edit_under_the_operation_that_describes_it(string body, string operation)
    {
        await _hosts.ResetWritableAsync();
        using var client = await _hosts.Writable.CreateSignedInClientAsync();

        var response = await client.PutAsync("/api/v1/portal", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await System.IO.File.ReadAllLinesAsync(Path.Combine(_hosts.Folder, "catalog.audit.jsonl"), Token);
        JsonDocument.Parse(audit.Last()).RootElement.GetProperty("operation").GetString().Should().Be(operation);
    }
}
