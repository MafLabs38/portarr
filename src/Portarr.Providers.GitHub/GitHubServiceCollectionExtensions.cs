using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Maintenance;

namespace Portarr.Providers.GitHub;

public static class GitHubServiceCollectionExtensions
{
    /// <summary>Largest GitHub API response read (a page of 100 alerts or repositories).</summary>
    private const int MaxResponseBytes = 8 * 1024 * 1024;

    public static IServiceCollection AddGitHubProvider(this IServiceCollection services)
    {
        services.AddHttpClient(GitHubRepositorySource.HttpClientName, client =>
        {
            client.BaseAddress = GitHubRepositorySource.ApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(20);
            client.MaxResponseContentBufferSize = MaxResponseBytes;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Portarr");
        });
        services.AddSingleton<ICodeRepositorySource, GitHubRepositorySource>();

        return services;
    }
}
