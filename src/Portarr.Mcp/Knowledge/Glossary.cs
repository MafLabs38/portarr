namespace Portarr.Mcp.Knowledge;

/// <summary>The vocabulary of Portarr's answers (spec §4, §5, V3, V4), for an assistant that reads them.</summary>
public static class Glossary
{
    public const string Markdown = """
        # Portarr glossary

        Portarr is a read-only portal over a homelab: it observes the infrastructure through its connectors (Docker,
        Kubernetes, Cloudflare, GitHub, registries) and never acts on it. To change anything, the operator uses their
        own tools; suggest the change, do not claim it was made.

        ## Entities

        - **Environment**: a Docker host, a Kubernetes cluster or a hosting account (Cloudflare). It may run as a VM on
          another environment (its hypervisor).
        - **Machine**: a Docker host or a Kubernetes node, with its CPU (cores) and memory (GiB) capacity and usage.
        - **Workload**: a running unit discovered by a connector (a container, a pod). Never declared by hand.
        - **App**: what the operator uses (a web UI, a service), declared in the catalog and linked to its workloads.
        - **Stack**: a group of apps that work together (a media stack).
        - **Data asset**: something whose loss loses state (a database, a volume, an app's files) that the operator
          chose to follow in the catalog, linked to an app. Only declared data is shown; nothing is discovered.
        - **Importance**: critical, important or comfort; it sets the accepted loss window (24 h, 7 days, 30 days).
        - **Copy**: local (the array) or off-site (a bucket), each expected by the catalog and proved by a backup tool.

        ## Statuses (best to worst)

        - **Healthy**: works. **Maintenance**: under planned maintenance. **Paused**: paused on purpose.
        - **Unknown**: Portarr cannot tell (environment unreachable, not checked yet).
        - **Degraded**: works partly (an optional part failed, a non-critical check fails).
        - **Down**: does not work (a critical workload stopped, a critical check fails).

        Every status comes with a **reason** ("Exited (1) 2 minutes ago", "http-502", "Healthcheck failing").

        ## Roles

        - **critical**: the app (or stack) does not work without it; its failure makes the whole Down.
        - **optional**: its failure only degrades the whole.

        ## Connections and reason codes

        - Environment connection: **connected**, **unreachable**, **pending** (not read yet), **not-connected** (no connector).
        - Reason codes: `timeout`, `unreachable`, `invalid-response`, `http-<code>`, `dns-failure`, `connection-failed`,
          `tls-invalid`, `tls-expired`, `keyword-missing`, `rate-limited`, `not-configured`.
        - Machine usage scope: **host** (the whole machine) or **containers** (a Docker host only sees the sum of its
          containers, a lower bound of its real load).

        ## Data coverage

        - Per expected copy: **protected** (a success within the accepted loss window), **late** (the newest success is
          older), **failed** (the last attempt failed after the last success), **missing** (declared, and no job
          produces it), **waiting** (the job exists and has not run yet, with its next run when the schedule tells:
          pending, not a failure), **not-set-up** (the backup tool is declared but not configured on the environment:
          the operator must fix it), **unknown** (the backup tool cannot be read: unreachable, refused).
        - An asset takes the worst state of its copies. "x of y critical assets have an off-site copy" is the figure
          that matters most; critical assets whose off-site job has not run yet are counted apart as pending.

        ## Maintenance

        - Repository security features: **on** (with alert counts by severity: critical, high, medium, low; "quality"
          rules are not vulnerabilities), **off** (turned off on GitHub: itself a finding), **unknown** (the token cannot read it).
        - Image verdicts: **up-to-date**, **behind** (a newer tag of the same shape: patch, minor, major or build),
          **newer-build** (a floating tag was rebuilt), **floating** (cannot tell), **unknown** (with a reason:
          `image-id`, `not-found`, `refused`, `rate-limited`), **pending** (not looked up yet).
        - Attention kinds: `severe-alerts`, `secret-alerts`, `ci-failing`, `image-major`, `images-behind`,
          `image-rebuilt`, `dependabot-off`, `code-scanning-off`, `other-alerts`, `dependabot-prs`, `images-unknown`.

        ## Overview finding kinds

        `app-down`, `app-degraded`, `app-unknown`, `environment-unreachable`, `machine-not-ready`,
        `metrics-unavailable`, `backup-failed`, `data-missing`, `backup-late`, `backup-tool-not-set-up`, `critical-without-offsite`, `critical-offsite-pending`, `catalog-errors`, and the
        maintenance kinds above.
        """;
}
