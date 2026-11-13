using System.Net;
using Portarr.Core.Platforms;
using Portarr.Test.Common;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Platforms.Test;

public sealed class EndOfLifeDateSourceContractFixture : SupportLifecycleSourceContract
{
    protected override SupportLifecycleHarness Arrange()
    {
        var broken = false;
        var http = new StubHttp(request =>
        {
            if (broken)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return request.RequestUri!.AbsolutePath == "/api/v1/products/ubuntu/" ? StubHttp.Json(EndOfLifeDateSourceFixture.Ubuntu) : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        return new SupportLifecycleHarness(new EndOfLifeDateSource(http), "ubuntu", "unraid-os", () => broken = true);
    }
}

public sealed class EndOfLifeDateSourceFixture
{
    /// <summary>The shape of the real answer: releases with their support days, some without a date or with a boolean.</summary>
    internal const string Ubuntu = """
        { "schema_version": "1.2.0", "result": { "name": "ubuntu", "releases": [
          { "name": "26.04", "isEol": false, "eoasFrom": "2031-05-29", "eolFrom": "2031-05-29" },
          { "name": "25.10", "isEol": false, "eoasFrom": "2026-07-01", "eolFrom": "2026-07-01" },
          { "name": "24.04", "isEol": false, "eoasFrom": "2029-04-25", "eolFrom": null },
          { "name": "22.04", "isEol": false, "eoasFrom": false, "eolFrom": "not a date" },
          { "name": "", "eolFrom": "2020-01-01" },
          { "eolFrom": "2020-01-01" }
        ] } }
        """;

    [Fact]
    public async Task should_read_the_support_days_of_each_release()
    {
        var source = new EndOfLifeDateSource(new StubHttp(_ => StubHttp.Json(Ubuntu)));

        var cycles = await source.ReadAsync("ubuntu", TestContext.Current.CancellationToken);

        cycles.Should().Equal(
            new SupportCycle("26.04", new DateOnly(2031, 5, 29), new DateOnly(2031, 5, 29)),
            new SupportCycle("25.10", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)),
            new SupportCycle("24.04", new DateOnly(2029, 4, 25), null),
            new SupportCycle("22.04", null, null));
    }

    [Fact]
    public async Task should_ask_the_product_path_with_a_get_and_answer_nothing_for_a_404()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cycles = await new EndOfLifeDateSource(http).ReadAsync("unraid-os", TestContext.Current.CancellationToken);

        cycles.Should().BeNull();
        http.Requests.Should().Equal(new Uri("https://endoflife.date/api/v1/products/unraid-os/"));
    }

    [Theory]
    [InlineData("rocky", "rocky-linux")]
    [InlineData("amzn", "amazon-linux")]
    [InlineData("ol", "oracle-linux")]
    [InlineData("ubuntu", "ubuntu")]
    public async Task should_ask_the_name_the_site_gives_a_distribution_the_machine_reported_by_its_os_release_id(string reported, string site)
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await new EndOfLifeDateSource(http).ReadAsync(reported, TestContext.Current.CancellationToken);

        http.Requests.Should().Equal(new Uri($"https://endoflife.date/api/v1/products/{site}/"));
    }

    [Fact]
    public async Task should_refuse_an_answer_larger_than_the_cap()
    {
        var huge = "{ \"result\": { \"releases\": [" + string.Join(',', Enumerable.Repeat("""{ "name": "26.04" }""", PlatformsHttp.MaxResponseBytes / 10)) + "] } }";
        var source = new EndOfLifeDateSource(new StubHttp(_ => StubHttp.Json(huge)));

        var act = () => source.ReadAsync("ubuntu", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task should_refuse_an_answer_without_releases()
    {
        var source = new EndOfLifeDateSource(new StubHttp(_ => StubHttp.Json("""{ "result": { "name": "x" } }""")));

        var act = () => source.ReadAsync("ubuntu", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
