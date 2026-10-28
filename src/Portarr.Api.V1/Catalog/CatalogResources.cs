using Portarr.Core.Catalog;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Catalog;

public sealed record StatusResource(string Status, string Reason)
{
    public static StatusResource From(ResolvedStatus status) => new(status.Status.ToString(), status.Reason);
}

public sealed record AppUrlResource(string Label, string Href);

public sealed record AppStackResource(string Id, string Name, string Role);

public sealed record AppWorkloadResource(string EnvironmentId, string Name, bool Found, StatusResource Status);

public sealed record AppResource(
    string Id,
    string Name,
    string Type,
    string? Icon,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AppUrlResource> Urls,
    AppStackResource? Stack,
    StatusResource Status,
    IReadOnlyList<AppWorkloadResource> Workloads);

public sealed record StackMemberResource(string AppId, string Name, string Role, StatusResource Status);

public sealed record StackResource(string Id, string Name, string? Description, StatusResource Status, IReadOnlyList<StackMemberResource> Members);

public sealed record CatalogStatusResource(
    string FilePath,
    bool FileExists,
    DateTimeOffset? LoadedAt,
    IReadOnlyList<string> Errors,
    int Apps,
    int Stacks,
    int Environments);

public static class CatalogResourceMapper
{
    public static AppResource ToResource(this AppView view)
    {
        var app = view.App;

        return new AppResource(
            app.Id,
            app.Name,
            app.Type.ToString().ToLowerInvariant(),
            app.Icon,
            app.Description,
            app.Tags,
            app.Urls.Select(url => new AppUrlResource(url.Label, url.Href.ToString())).ToList(),
            view.Stack is null ? null : new AppStackResource(view.Stack.Id, view.Stack.Name, view.Role!.Value.ToString().ToLowerInvariant()),
            StatusResource.From(view.Status),
            view.Workloads.Select(w => new AppWorkloadResource(w.EnvironmentId, w.Name, w.Workload is not null, StatusResource.From(w.Status))).ToList());
    }

    public static StackResource ToResource(this StackView view)
    {
        return new StackResource(
            view.Stack.Id,
            view.Stack.Name,
            view.Stack.Description,
            StatusResource.From(view.Status),
            view.Members.Select(m => new StackMemberResource(m.Member.AppId, m.AppName, m.Member.Role.ToString().ToLowerInvariant(), StatusResource.From(m.Status))).ToList());
    }

    public static CatalogStatusResource ToResource(this CatalogState state)
    {
        return new CatalogStatusResource(
            state.FilePath,
            state.FileExists,
            state.LoadedAt,
            state.Errors,
            state.Catalog.Apps.Count,
            state.Catalog.Stacks.Count,
            state.Catalog.Environments.Count);
    }
}
