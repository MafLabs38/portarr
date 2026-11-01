using Portarr.Core.Backups;
using Portarr.Core.Catalog;
using Portarr.Core.Data;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Data;

/// <summary>The declared data policy compared with what the backup tools report (D-77 to D-79).</summary>
public sealed class DataServiceFixture
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private const string Catalog = """
        environments:
          - { id: server-1, docker: { url: "http://10.0.0.10:2375" } }
          - { id: cluster, kubernetes: { server: "https://10.0.0.20:6443" } }
        apps:
          - { id: shop }
          - { id: media }
        """;

    [Fact]
    public void should_judge_a_copy_by_its_newest_success_within_the_accepted_loss_window()
    {
        var reference = new BackupSourceReference(BackupSourceKinds.Backrest, "server-1", "plan");
        var window = TimeSpan.FromHours(24);

        State(reference, Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z"), window).Should().Be(CopyState.Protected);
        State(reference, Run("2026-01-30T11:30:00Z", BackupResult.Success, "2026-01-30T11:30:00Z"), window).Should().Be(CopyState.Protected, "a one hour grace absorbs a run that drifts");
        State(reference, Run("2026-01-30T09:00:00Z", BackupResult.Success, "2026-01-30T09:00:00Z"), window).Should().Be(CopyState.Late);
        State(reference, Run("2026-01-31T03:00:00Z", BackupResult.Failed, "2026-01-30T03:00:00Z"), window).Should().Be(CopyState.Failed);
        State(reference, Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z"), window).Should().Be(CopyState.Protected, "a success after an old failure is a success");
        State(reference, null, window).Should().Be(CopyState.Waiting, "a plan that never ran is pending, not unknown");
    }

    [Fact]
    public void should_tell_a_tool_that_cannot_be_read_from_a_job_it_does_not_have()
    {
        var reference = new BackupSourceReference(BackupSourceKinds.K8up, "cluster", "shop/offsite");
        var window = TimeSpan.FromDays(7);

        DataService.Check(reference, new SourceReading("k8up", "cluster", [], BackupMonitor.NoConnector, Now, NotSetUp: true), window, Now).Should().Match<SourceCheck>(c => c.State == CopyState.NotSetUp && c.Reason == "no-connector");
        DataService.Check(reference, new SourceReading("k8up", "cluster", [], "http-403", Now), window, Now).Should().Match<SourceCheck>(c => c.State == CopyState.Unknown && c.Reason == "http-403");
        DataService.Check(reference, new SourceReading("k8up", "cluster", [], null, Now), window, Now).Should().Match<SourceCheck>(c => c.State == CopyState.Missing && c.Reason == "not-found");
    }

    [Fact]
    public void should_narrow_a_k8up_schedule_to_the_snapshots_that_hold_the_item()
    {
        var snapshots = new[]
        {
            new BackupSnapshot("a1", At("2026-01-31T04:00:00Z"), ["/data/other"]),
            new BackupSnapshot("b2", At("2026-01-30T04:00:00Z"), ["/data/data-db-0"]),
        };
        var job = new BackupJob("shop/offsite", "offsite", null, ["b2"], new BackupRun(At("2026-01-31T04:00:00Z"), BackupResult.Success, At("2026-01-31T04:00:00Z")), snapshots);
        var reading = new SourceReading("k8up", "cluster", [job], null, Now);

        var held = DataService.Check(new BackupSourceReference("k8up", "cluster", "shop/offsite", "data-db-0"), reading, TimeSpan.FromDays(7), Now);
        var absent = DataService.Check(new BackupSourceReference("k8up", "cluster", "shop/offsite", "n8n-data"), reading, TimeSpan.FromDays(7), Now);

        held.Should().Match<SourceCheck>(c => c.State == CopyState.Protected && c.Run!.LastSuccessAt == At("2026-01-30T04:00:00Z") && c.Run.SnapshotId == "b2");
        absent.Should().Match<SourceCheck>(c => c.State == CopyState.Missing && c.Reason == "not-in-snapshots");
    }

    [Fact]
    public void should_take_the_best_source_of_a_copy_and_the_worst_copy_of_an_asset()
    {
        var report = Build("""
            data:
              - id: shop-db
                app: shop
                kind: postgresql
                where: { environment: cluster, namespace: shop, workload: db-0 }
                importance: critical
                targets:
                  local: { cronjob: shop/dump, manual: "2026-01-31T01:00:00Z" }
                  offsite: { k8up: { schedule: shop/offsite } }
            """,
            Reading("cronjob", "cluster", Job("shop/dump", Run("2026-01-31T03:00:00Z", BackupResult.Failed, "2026-01-29T03:00:00Z"))),
            Reading("k8up", "cluster", Job("shop/offsite", Run("2026-01-31T04:00:00Z", BackupResult.Success, "2026-01-31T04:00:00Z"))));

        var asset = report.Assets.Single();
        asset.Local!.State.Should().Be(CopyState.Protected, "the hand-made copy of last night covers the failed CronJob");
        asset.Local.Sources.Single().State.Should().Be(CopyState.Failed);
        asset.Offsite!.State.Should().Be(CopyState.Protected);
        asset.State.Should().Be(CopyState.Protected);
        report.Summary.Should().Be(new DataSummary(1, 1, 0, 0, 0, 0, 0, 1, 1));
    }

    [Fact]
    public void should_put_what_is_at_risk_first_and_count_critical_assets_without_an_offsite_copy()
    {
        var report = Build("""
            data:
              - { id: calm, app: media, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: { manual: "2026-01-20T00:00:00Z" } } }
              - { id: gone, app: shop, kind: postgresql, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: gone-local } }, offsite: {} } }
              - { id: late, app: shop, kind: files, importance: important, where: { environment: server-1 }, targets: { local: { backrest: { plan: late-local } } } }
              - { id: fine, app: media, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: fine-local } }, offsite: { backrest: { plan: fine-offsite } } } }
              - { id: dark, app: media, kind: files, importance: important, where: { environment: server-1 }, targets: { local: { k8up: { schedule: a/b, environment: cluster } } } }
            """,
            Reading("backrest", "server-1",
                Job("late-local", Run("2026-01-20T03:00:00Z", BackupResult.Success, "2026-01-20T03:00:00Z")),
                Job("fine-local", Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z")),
                Job("fine-offsite", Run("2026-01-31T03:30:00Z", BackupResult.Success, "2026-01-31T03:30:00Z"))),
            new SourceReading("k8up", "cluster", [], "http-403", Now));

        report.Assets.Select(a => (a.Asset.Id, a.State)).Should().Equal(
            ("gone", CopyState.Missing), ("late", CopyState.Late), ("dark", CopyState.Unknown), ("fine", CopyState.Protected), ("calm", CopyState.Protected));
        report.Assets[0].Offsite!.Reason.Should().Be("no-source", "an offsite copy is declared and nothing produces it");
        report.Summary.Should().Be(new DataSummary(5, 2, 1, 0, 1, 1, 2, 2, 1));
        report.Sources.Select(s => (s.Kind, s.Error)).Should().Equal(("backrest", null), ("k8up", "http-403"));
    }

    [Fact]
    public void should_ask_the_operator_to_set_up_a_tool_whose_reading_says_so()
    {
        var reference = new BackupSourceReference(BackupSourceKinds.Backrest, "server-1", "plan");

        var check = DataService.Check(reference, new SourceReading("backrest", "server-1", [], "credentials-missing", Now, NotSetUp: true), TimeSpan.FromHours(24), Now);

        check.Should().Match<SourceCheck>(c => c.State == CopyState.NotSetUp && c.Reason == "credentials-missing");
    }

    [Fact]
    public void should_leave_a_source_not_read_yet_unknown_not_unconfigured()
    {
        var reference = new BackupSourceReference(BackupSourceKinds.Backrest, "server-1", "plan");

        var check = DataService.Check(reference, null, TimeSpan.FromHours(24), Now);

        check.Should().Match<SourceCheck>(c => c.State == CopyState.Unknown && c.Reason == DataService.NotReadYet);
    }

    [Theory]
    [InlineData("http-401")]
    [InlineData("http-403")]
    [InlineData("unreachable")]
    [InlineData("timeout")]
    [InlineData("invalid-response")]
    public void should_leave_a_tool_that_failed_to_answer_unknown(string reason)
    {
        var reference = new BackupSourceReference(BackupSourceKinds.Backrest, "server-1", "plan");

        var check = DataService.Check(reference, new SourceReading("backrest", "server-1", [], reason, Now), TimeSpan.FromHours(24), Now);

        check.Should().Match<SourceCheck>(c => c.State == CopyState.Unknown && c.Reason == reason);
    }

    [Fact]
    public void should_wait_for_the_first_run_of_a_job_that_exists_and_say_when_it_fires_next()
    {
        var reference = new BackupSourceReference(BackupSourceKinds.Backrest, "server-1", "plan");
        var job = new BackupJob("plan", "plan", "0 3 * * *", ["offsite"], null);
        var reading = new SourceReading("backrest", "server-1", [job], null, Now);

        var check = DataService.Check(reference, reading, TimeSpan.FromHours(24), Now);

        check.Should().Match<SourceCheck>(c => c.State == CopyState.Waiting && c.Reason == "no-run" && c.Schedule == "0 3 * * *");
        check.NextRunAt.Should().Be(At("2026-02-01T03:00:00Z"));
    }

    [Theory]
    [InlineData("every 2 d")]
    [InlineData("disabled")]
    [InlineData(null)]
    public void should_not_invent_a_next_run_when_the_schedule_is_not_a_cron_expression(string? schedule)
    {
        var reference = new BackupSourceReference(BackupSourceKinds.Backrest, "server-1", "plan");
        var reading = new SourceReading("backrest", "server-1", [new BackupJob("plan", "plan", schedule, [], null)], null, Now);

        var check = DataService.Check(reference, reading, TimeSpan.FromHours(24), Now);

        check.Should().Match<SourceCheck>(c => c.State == CopyState.Waiting && c.NextRunAt == null);
    }

    [Fact]
    public void should_wait_when_a_job_only_has_attempts_that_are_neither_a_success_nor_a_failure()
    {
        var reference = new BackupSourceReference(BackupSourceKinds.K8up, "cluster", "shop/offsite");
        var neither = new BackupRun(null, null, null);

        State(reference, neither, TimeSpan.FromDays(7)).Should().Be(CopyState.Waiting);
    }

    [Fact]
    public void should_not_count_a_waiting_or_not_set_up_copy_as_at_risk_and_keep_them_apart_in_the_summary()
    {
        var report = Build("""
            data:
              - { id: waits, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: waits-local } }, offsite: { backrest: { plan: waits-offsite } } } }
              - { id: unset, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { k8up: { schedule: a/b, environment: cluster } } } }
              - { id: broken, app: media, kind: files, importance: important, where: { environment: cluster }, targets: { local: { backrest: { plan: x, environment: server-1 } }, offsite: { cronjob: a/dump } } }
            """,
            Reading("backrest", "server-1",
                Job("waits-local", Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z")),
                new BackupJob("waits-offsite", "waits-offsite", "0 4 * * *", ["b2"], null),
                new BackupJob("x", "x", null, [], new BackupRun(null, null, At("2026-01-31T03:00:00Z")))),
            new SourceReading("k8up", "cluster", [], BackupMonitor.NoConnector, Now, NotSetUp: true),
            new SourceReading("cronjob", "cluster", [], "unreachable", Now));

        report.Assets.Select(a => (a.Asset.Id, a.State)).Should().Equal(
            ("unset", CopyState.NotSetUp), ("broken", CopyState.Unknown), ("waits", CopyState.Waiting));
        report.Assets[2].Offsite!.NextRunAt.Should().Be(At("2026-02-01T04:00:00Z"));
        report.Assets[2].Local!.NextRunAt.Should().BeNull("a protected copy has no pending run to announce");

        report.Summary.AtRisk.Should().Be(0, "nothing proves a loss");
        report.Summary.Should().Be(new DataSummary(3, 0, 0, 0, 0, 1, 0, 2, 0, Waiting: 1, NotSetUp: 1, CriticalOffsitePending: 1));
    }

    [Fact]
    public void should_let_a_proven_source_outweigh_a_waiting_or_unconfigured_one_and_a_gap_outweigh_a_wait()
    {
        var report = Build("""
            data:
              - { id: covered, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: ok, environment: server-1 }, k8up: { schedule: a/b, environment: cluster } }, offsite: { backrest: { plan: later } } } }
              - { id: gap, app: shop, kind: files, importance: critical, where: { environment: cluster }, targets: { local: { backrest: { plan: later, environment: server-1 } }, offsite: { cronjob: a/nothing } } }
            """,
            Reading("backrest", "server-1",
                Job("ok", Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z")),
                new BackupJob("later", "later", null, [], null)),
            new SourceReading("k8up", "cluster", [], BackupMonitor.NoConnector, Now, NotSetUp: true),
            Reading("cronjob", "cluster"));

        var covered = report.Assets.Single(a => a.Asset.Id == "covered");
        covered.Local!.State.Should().Be(CopyState.Protected, "one source proves the copy, the other one being unconfigured does not matter");
        covered.Offsite!.State.Should().Be(CopyState.Waiting);
        covered.State.Should().Be(CopyState.Waiting);
        report.Assets.Single(a => a.Asset.Id == "gap").State.Should().Be(CopyState.Missing, "a job the tool does not have is worse than one that waits");
        report.Assets[0].Asset.Id.Should().Be("gap", "what is wrong goes before what waits");
    }

    [Theory]
    [InlineData("critical", 24)]
    [InlineData("important", 24 * 7)]
    [InlineData("comfort", 24 * 30)]
    public void should_derive_the_loss_window_from_the_importance_unless_max_age_says_otherwise(string importance, double hours)
    {
        var parsed = TestCatalog.Parse(Catalog + $"\ndata:\n  - {{ id: a, app: shop, kind: files, importance: {importance}, where: {{ environment: server-1 }}, targets: {{ local: {{}} }} }}\n"
            + $"  - {{ id: b, app: shop, kind: files, importance: {importance}, maxAge: 36h, where: {{ environment: server-1 }}, targets: {{ local: {{}} }} }}\n");

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.DataAssets[0].MaxAge.TotalHours.Should().Be(hours);
        parsed.Catalog.DataAssets[1].MaxAge.Should().Be(TimeSpan.FromHours(36));
    }

    [Fact]
    public void should_read_the_data_section_with_its_sources_and_the_backrest_of_an_environment()
    {
        var parsed = TestCatalog.Parse("""
            environments:
              - { id: server-1, docker: { url: "http://10.0.0.10:2375" }, backrest: { url: "http://10.0.0.10:9898", allowInsecure: true, userEnv: BACKREST_USER, passwordEnv: BACKREST_PASSWORD } }
              - { id: cluster, kubernetes: { server: "https://10.0.0.20:6443" } }
            apps:
              - { id: shop }
            data:
              - id: shop-db
                name: Shop database
                app: shop
                kind: postgresql
                where: { environment: cluster, namespace: shop, workload: db-0, claim: data-db-0 }
                importance: critical
                targets:
                  local: { cronjob: shop/dump }
                  offsite: { k8up: { schedule: shop/offsite, item: data-db-0 }, backrest: { plan: shop-offsite, environment: server-1 }, manual: true }
            """);

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.Environments[0].Declares("backrest").Should().BeTrue();
        var data = parsed.Catalog.DataAssets.Single();
        data.Where.Should().Be(new DataLocation("cluster", "shop", "db-0", "data-db-0", null));
        data.Targets[0].Sources.Should().Equal(new BackupSourceReference("cronjob", "cluster", "shop/dump"));
        data.Targets[1].Sources.Should().Equal(
            new BackupSourceReference("backrest", "server-1", "shop-offsite"),
            new BackupSourceReference("k8up", "cluster", "shop/offsite", "data-db-0"));
        data.Targets[1].Manual.Should().Be(new ManualCopy(null));
    }

    [Theory]
    [InlineData("{ id: a, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: {} } }", "app '' is not a declared app")]
    [InlineData("{ id: a, app: nope, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: {} } }", "app 'nope' is not a declared app")]
    [InlineData("{ id: a, app: shop, importance: comfort, where: { environment: server-1 }, targets: { local: {} } }", "kind is required")]
    [InlineData("{ id: a, app: shop, kind: files, importance: vital, where: { environment: server-1 }, targets: { local: {} } }", "importance is required")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: nowhere }, targets: { local: {} } }", "where.environment 'nowhere'")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, maxAge: 1w, where: { environment: server-1 }, targets: { local: {} } }", "maxAge '1w' must be a duration")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: server-1 } }", "targets must name the copies")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: server-1 }, targets: { tape: {} } }", "target 'tape' must be local or offsite")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: { k8up: { schedule: a/b } } } }", "is read on a kubernetes environment")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: cluster }, targets: { local: { backrest: { plan: p } } } }", "is read on a machine's environment")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: { backrest: { plan: p, environment: nowhere } } } }", "environment 'nowhere'")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: cluster }, targets: { local: { cronjob: no-namespace } } }", "cronjob must be <namespace>/<cronjob name>")]
    [InlineData("{ id: a, app: shop, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: { manual: yesterday } } }", "manual must be true")]
    public void should_reject_an_invalid_data_entry(string entry, string error)
    {
        var result = TestCatalog.Parse(Catalog + $"\ndata:\n  - {entry}\n");

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_reject_the_retired_backups_section()
    {
        TestCatalog.Parse("backups:\n  - { id: old }\n").IsValid.Should().BeFalse("the backups section was replaced by data");
    }

    private const string ManualAsset = """
        data:
          - { id: notes, app: shop, kind: files, importance: critical, maxAge: 7d, where: { environment: server-1 }, targets: { local: { manual: "%DATE%" } } }
        """;

    [Theory]
    [InlineData("2026-01-30T00:00:00Z", CopyState.Protected, null)]
    [InlineData("2025-12-01T00:00:00Z", CopyState.Late, null)]
    [InlineData("2026-03-01T00:00:00Z", CopyState.Unknown, "manual-in-the-future")]
    public void should_judge_a_copy_the_operator_makes_by_hand_against_the_window_and_refuse_a_date_in_the_future(string date, CopyState state, string? reason)
    {
        var report = Build(ManualAsset.Replace("%DATE%", date, StringComparison.Ordinal));

        var local = report.Assets.Single().Local!;

        local.State.Should().Be(state);
        local.Reason.Should().Be(reason);
    }

    [Fact]
    public void should_leave_a_manual_copy_without_a_date_unknown_and_the_asset_without_a_size()
    {
        var report = Build("""
            data:
              - { id: notes, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { manual: true } } }
            """);

        report.Assets.Single().Local.Should().Match<CopyView>(c => c.State == CopyState.Unknown && c.Reason == "manual" && c.LastSuccessAt == null && c.SizeBytes == null);
    }

    [Fact]
    public void should_call_a_copy_with_no_source_missing_and_a_local_only_asset_without_an_offsite_copy()
    {
        var report = Build("""
            data:
              - { id: orphan, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: {} } }
              - { id: local-only, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: ok } } } }
            """,
            Reading("backrest", "server-1", Job("ok", Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z"))));

        var orphan = report.Assets.Single(a => a.Asset.Id == "orphan");
        var localOnly = report.Assets.Single(a => a.Asset.Id == "local-only");

        orphan.Local.Should().Match<CopyView>(c => c.State == CopyState.Missing && c.Reason == "no-source");
        localOnly.Offsite.Should().BeNull();
        localOnly.State.Should().Be(CopyState.Protected);
    }

    [Fact]
    public void should_order_the_assets_by_state_then_importance_then_app_then_name()
    {
        var report = Build("""
            data:
              - { id: b-comfort, name: B, app: shop, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: { backrest: { plan: ok } } } }
              - { id: a-critical, name: A, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: ok } } } }
              - { id: c-critical, name: C, app: shop, kind: files, importance: critical, where: { environment: server-1 }, targets: { local: { backrest: { plan: ok } } } }
              - { id: gone, name: Gone, app: shop, kind: files, importance: comfort, where: { environment: server-1 }, targets: { local: { backrest: { plan: absent } } } }
            """,
            Reading("backrest", "server-1", Job("ok", Run("2026-01-31T03:00:00Z", BackupResult.Success, "2026-01-31T03:00:00Z"))));

        report.Assets.Select(a => a.Asset.Id).Should().Equal("gone", "a-critical", "c-critical", "b-comfort");
    }

    [Fact]
    public void should_match_an_item_by_whole_path_segments_not_by_a_substring()
    {
        var job = new BackupJob("shop/offsite", "offsite", null, [], new BackupRun(At("2026-01-31T04:00:00Z"), BackupResult.Success, At("2026-01-31T04:00:00Z")),
            [new BackupSnapshot("old", At("2026-01-31T04:00:00Z"), ["/data/db-old"]), new BackupSnapshot("new", At("2026-01-30T04:00:00Z"), ["/data/db/pg"])]);
        var reading = new SourceReading("k8up", "cluster", [job], null, Now);

        var db = DataService.Check(new BackupSourceReference("k8up", "cluster", "shop/offsite", "db"), reading, TimeSpan.FromDays(7), Now);
        var gone = DataService.Check(new BackupSourceReference("k8up", "cluster", "shop/offsite", "d"), reading, TimeSpan.FromDays(7), Now);

        db.Run!.SnapshotId.Should().Be("new", "'db-old' is not 'db'");
        gone.State.Should().Be(CopyState.Missing, "'d' is a part of a name, not a path segment");
    }

    private static DataReport Build(string data, params SourceReading[] readings)
    {
        var parsed = TestCatalog.Parse(Catalog + "\n" + data);
        parsed.Errors.Should().BeEmpty();
        return DataService.Build(parsed.Catalog, new BackupMonitorState(readings.ToDictionary(r => r.Key)), Now);
    }

    private static CopyState State(BackupSourceReference reference, BackupRun? run, TimeSpan window)
    {
        var reading = new SourceReading(reference.Kind, reference.EnvironmentId, [new BackupJob(reference.Reference, reference.Reference, null, [], run)], null, Now);
        return DataService.Check(reference, reading, window, Now).State;
    }

    private static SourceReading Reading(string kind, string environment, params BackupJob[] jobs) => new(kind, environment, jobs, null, Now);

    private static BackupJob Job(string reference, BackupRun run) => new(reference, reference, null, [], run);

    private static BackupRun Run(string at, BackupResult result, string success) => new(At(at), result, At(success));

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
