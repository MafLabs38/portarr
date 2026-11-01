using System.Globalization;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Cloudflare;

/// <summary>Cloudflare Pages projects and Workers as workloads (D-53).</summary>
internal static class CloudflareMapper
{
    public const string PagesImage = "Cloudflare Pages";
    public const string WorkerImage = "Cloudflare Worker";
    public const string KindLabel = "cloudflare.kind";
    public const string BranchLabel = "cloudflare.branch";
    public const string CommitLabel = "cloudflare.commit";

    private const int ShortCommitLength = 7;

    /// <summary>
    /// State of the latest production deployment: success → running, failure or canceled → exited,
    /// anything else (queued, building, deploying) → created. Without one, the live (canonical) deployment.
    /// </summary>
    public static Workload ToWorkload(string environmentId, PagesProject project)
    {
        var deployment = project.LatestDeployment is { Environment: "production" } latest ? latest : project.CanonicalDeployment;
        var status = deployment?.LatestStage?.Status;
        var state = status switch
        {
            null => WorkloadState.Unknown,
            "success" => WorkloadState.Running,
            "failure" or "canceled" => WorkloadState.Exited,
            _ => WorkloadState.Created,
        };

        var commit = ShortCommit(deployment?.DeploymentTrigger?.Metadata?.CommitHash);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { [KindLabel] = "pages" };
        var branch = deployment?.DeploymentTrigger?.Metadata?.Branch ?? project.ProductionBranch;
        if (branch is not null)
        {
            labels[BranchLabel] = branch;
        }

        if (commit is not null)
        {
            labels[CommitLabel] = commit;
        }

        var text = state switch
        {
            WorkloadState.Running => $"Deployed {Date(deployment!.CreatedOn)}",
            WorkloadState.Exited => $"Deployment {(status == "canceled" ? "canceled" : "failed")} {Date(deployment!.CreatedOn)}",
            WorkloadState.Created => $"Deploying ({deployment!.LatestStage?.Name ?? status})",
            _ => "No production deployment",
        };

        return new Workload(
            environmentId,
            $"pages/{project.Name}",
            project.Name,
            PagesImage,
            state,
            WorkloadHealth.None,
            commit is null ? text : $"{text} · {commit}",
            project.CreatedOn ?? DateTimeOffset.UnixEpoch,
            null,
            labels);
    }

    /// <summary>A Worker listed by the account is deployed: running, with its last deployment date.</summary>
    public static Workload ToWorkload(string environmentId, WorkerScript script)
    {
        return new Workload(
            environmentId,
            $"workers/{script.Id}",
            script.Id,
            WorkerImage,
            WorkloadState.Running,
            WorkloadHealth.None,
            $"Deployed {Date(script.ModifiedOn ?? script.CreatedOn)}",
            script.CreatedOn ?? DateTimeOffset.UnixEpoch,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { [KindLabel] = "worker" });
    }

    private static string? ShortCommit(string? hash)
    {
        return string.IsNullOrEmpty(hash) ? null : hash[..Math.Min(ShortCommitLength, hash.Length)];
    }

    private static string Date(DateTimeOffset? date)
    {
        return date?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "(date unknown)";
    }
}
