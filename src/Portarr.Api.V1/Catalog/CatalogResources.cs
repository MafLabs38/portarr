using Portarr.Api.V1.Checks;
using Portarr.Core.Catalog;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Catalog;

public sealed record StatusResource(string Status, string Reason)
{
    public static StatusResource From(ResolvedStatus status) => new(status.Status.ToString(), status.Reason);
}

/// <summary>An app URL: <c>Scope</c> is public or lan, <c>Derived</c> when built from a published port.</summary>
public sealed record AppUrlResource(string Label, string Href, string Scope, bool Derived);

public sealed record AppStackResource(string Id, string Name, string Role);

/// <summary>A workload of an app; <c>Runtime</c>: docker, cloudflare or none.</summary>
public sealed record AppWorkloadResource(
    string EnvironmentId,
    string Runtime,
    string Name,
    string Role,
    bool Found,
    string? State,
    string? Health,
    string? StatusText,
    StatusResource Status);

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
    IReadOnlyList<AppWorkloadResource> Workloads,
    AppCheckResource? Check);

public sealed record StackMemberResource(string AppId, string Name, string Role, StatusResource Status);

public sealed record StackResource(string Id, string Name, string? Description, StatusResource Status, IReadOnlyList<StackMemberResource> Members);

/// <summary>Catalog state. Only the file name is exposed: the absolute path stays on the server.</summary>
public sealed record CatalogStatusResource(
    string FileName,
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
            view.Icon,
            app.Description,
            app.Tags,
            view.Urls.Select(url => new AppUrlResource(url.Label, url.Href.ToString(), url.Scope.ToString().ToLowerInvariant(), url.Derived)).ToList(),
            view.Stack is null ? null : new AppStackResource(view.Stack.Id, view.Stack.Name, view.Role!.Value.ToString().ToLowerInvariant()),
            StatusResource.From(view.Status),
            view.Workloads.Select(w => new AppWorkloadResource(
                w.EnvironmentId,
                w.Runtime,
                w.Name,
                w.Role.ToString().ToLowerInvariant(),
                w.Workload is not null,
                w.Workload?.State.ToString().ToLowerInvariant(),
                w.Workload?.Health.ToString().ToLowerInvariant(),
                w.Workload?.StatusText,
                StatusResource.From(w.Status))).ToList(),
            view.Check?.ToResource());
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
            Path.GetFileName(state.FilePath),
            state.FileExists,
            state.LoadedAt,
            state.Errors,
            state.Catalog.Apps.Count,
            state.Catalog.Stacks.Count,
            state.Catalog.Environments.Count);
    }
}
