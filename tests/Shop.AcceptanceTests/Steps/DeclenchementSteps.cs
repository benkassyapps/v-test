using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Reqnroll;
using Shop.AcceptanceTests.Support;
using Shop.Domain.Catalog;
using Shop.Testing.Hosting;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes du déclenchement manuel, du planificateur, de l'exclusivité et du signalement (declenchement.feature).</summary>
[Binding]
public sealed class DeclenchementSteps(CatalogWorld world, ScenarioHttp http)
{
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(30);

    // --- Préparation de l'historique et de l'horloge ---

    [Given("il est {string}")]
    public void ItIs(string instant) => world.SetClock(At(instant));

    [Given("la dernière synchronisation réussie date du {string}")]
    public async Task LastSuccessAt(string instant)
    {
        world.SetClock(At(instant));
        await world.SyncDirectOrFailAsync();
    }

    [Given("une synchronisation planifiée a réussi aujourd'hui")]
    public async Task ScheduledSyncSucceededToday()
    {
        await world.CheckSchedulerAsync(instance: 1);
        var last = await world.LastRunAsync();
        Assert.Equal((SyncTrigger.Scheduled, SyncRunStatus.Succeeded), (last.Trigger, last.Status));
        world.AdvanceClock(TimeSpan.FromMinutes(10));
    }

    [Given("une synchronisation manuelle a réussi ensuite")]
    public async Task ManualSyncSucceededAfterwards()
    {
        world.AdvanceClock(TimeSpan.FromMinutes(10));
        await world.SyncDirectOrFailAsync();
    }

    [Given("une synchronisation planifiée a échoué le {string}")]
    public Task ScheduledSyncFailedAt(string instant) => InsertFailedScheduledRunAsync(At(instant));

    [Given("{int} synchronisations planifiées ont échoué consécutivement, la dernière le {string}")]
    public async Task ConsecutiveScheduledFailures(int count, string lastInstant)
    {
        var last = At(lastInstant);
        for (var index = count - 1; index >= 0; index--)
        {
            await InsertFailedScheduledRunAsync(last.AddMinutes(-index));
        }
    }

    [Given("les synchronisations suivantes ont échoué")]
    public async Task FollowingSyncsFailed()
    {
        var lastSuccess = (await world.LastRunAsync()).FinishedAt!.Value;
        await InsertFailedScheduledRunAsync(lastSuccess.AddDays(1));
    }

    [Given("le créneau du jour est échu sans synchronisation réussie")]
    public async Task SlotDueWithoutSuccess() =>
        Assert.DoesNotContain(await world.RunsAsync(), run => run.Status == SyncRunStatus.Succeeded);

    [Given("une synchronisation est en cours sur l'instance {int}")]
    public async Task SyncInProgress(int instance)
    {
        world.Provider.HoldRequests();
        // Client conservé pendant tout le scénario : le libérer annulerait la requête encore en cours.
        var client = world.Cluster.CreateClient(instance, admin: true);
        world.BackgroundClient = client;
        world.BackgroundSync = client.PostAsync(new Uri("/api/admin/catalog/sync", UriKind.Relative), content: null);
        await world.Provider.WaitForHeldRequestAsync(HoldTimeout);
    }

    // --- Déclenchement manuel ---

    [When("l'exploitant déclenche une synchronisation sur l'instance {int} avec la clé d'administration")]
    public async Task OperatorTriggersSync(int instance)
    {
        await world.CaptureBeforeAsync();
        await world.TriggerManualSyncAsync(instance);
    }

    [When("l'exploitant déclenche une synchronisation sur l'instance {int} avec la clé {string}")]
    public async Task OperatorTriggersSyncWithKey(int instance, string key)
    {
        await world.CaptureBeforeAsync();
        await world.TriggerManualSyncAsync(instance, adminKey: key.Length == 0 ? null : key);
    }

    [When("l'exploitant consulte l'historique des synchronisations")]
    public Task OperatorReadsHistory() => world.GetAdminAsync("/api/admin/catalog/sync-runs");

    [When("l'exploitant consulte l'état de la synchronisation")]
    public Task OperatorReadsStatus() => world.GetAdminAsync("/api/admin/catalog/sync-status");

    [When("l'état de la synchronisation est consulté sans clé d'administration")]
    public Task StatusWithoutKey() => world.GetAdminAsync("/api/admin/catalog/sync-status", withKey: false);

    // --- Planificateur ---

    [When("le planificateur de l'instance {int} effectue sa vérification")]
    public async Task SchedulerChecks(int instance)
    {
        await world.CaptureBeforeAsync();
        await world.CheckSchedulerAsync(instance);
    }

    [When("le planificateur de l'instance {int} effectue sa vérification avec le créneau du jour échu")]
    public async Task SchedulerChecksAfterNextSlot(int instance)
    {
        // Lendemain 02:10 UTC : le créneau suivant la dernière réussite est échu.
        var tomorrow = world.Now.UtcDateTime.Date.AddDays(1).AddHours(2).AddMinutes(10);
        world.SetClock(new DateTimeOffset(tomorrow, TimeSpan.Zero));
        await SchedulerChecks(instance);
    }

    [When("les planificateurs des instances {int} et {int} effectuent leur vérification simultanément")]
    public async Task SchedulersCheckConcurrently(int first, int second)
    {
        await world.CaptureBeforeAsync();
        await Task.WhenAll(world.CheckSchedulerAsync(first), world.CheckSchedulerAsync(second));
    }

    // --- Vérifications ---

    [Then("le résumé indique le statut {string}, le déclencheur {string}, l'instance {int}, les dates de début et de fin et {int} produits récupérés")]
    public void SummaryIndicates(string status, string trigger, int instance, int fetched)
    {
        var run = http.LastJson();
        Assert.Equal(status, run.GetProperty("status").GetString());
        Assert.Equal(trigger, run.GetProperty("trigger").GetString());
        Assert.Equal($"instance-{instance}", run.GetProperty("instanceId").GetString());
        Assert.Equal(JsonValueKind.String, run.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.String, run.GetProperty("finishedAt").ValueKind);
        Assert.Equal(fetched, run.GetProperty("fetched").GetInt32());
    }

    [Then("le résumé indique le statut {string} et le déclencheur {string}")]
    public void SummaryIndicatesStatusAndTrigger(string status, string trigger)
    {
        var run = http.LastJson();
        Assert.Equal((status, trigger), (run.GetProperty("status").GetString(), run.GetProperty("trigger").GetString()));
    }

    [Then("aucune synchronisation n'a été exécutée")]
    [Then("aucune nouvelle synchronisation n'a été exécutée")]
    public async Task NoNewSync() => Assert.Equal(world.RunCountBefore, (await world.RunsAsync()).Count);

    [Then("l'historique contient un run {string} dont la cause est renseignée")]
    public async Task HistoryHasRunWithCause(string status)
    {
        var last = await world.LastRunAsync();
        Assert.Equal(status, last.Status.ToString());
        Assert.False(string.IsNullOrWhiteSpace(last.Error));
    }

    [Then("l'historique contient dans l'ordre un run {string} puis un run {string}")]
    public void HistoryInOrder(string first, string second)
    {
        var items = http.LastJson().GetProperty("items").EnumerateArray().Select(item => item.GetProperty("trigger").GetString()).ToList();
        Assert.Equal([first, second], items.Take(2));
    }

    [Then("une synchronisation {string} a réussi sur l'instance {int}")]
    public async Task SyncSucceededOnInstance(string trigger, int instance)
    {
        var last = await world.LastRunAsync();
        Assert.Equal((trigger, SyncRunStatus.Succeeded, $"instance-{instance}"), (last.Trigger.ToString(), last.Status, last.InstanceId));
        Assert.True((await world.RunsAsync()).Count > world.RunCountBefore, "Aucune nouvelle synchronisation n'a été exécutée.");
    }

    [Then("l'instance {int} enregistre un run {string}")]
    public async Task InstanceRecordedRun(int instance, string status) =>
        Assert.Contains(await world.RunsAsync(), run => run.InstanceId == $"instance-{instance}" && run.Status.ToString() == status);

    [Then("une fois la synchronisation de l'instance {int} terminée, l'historique contient un seul run {string}")]
    public async Task AfterBackgroundSyncSingleRun(int instance, string status)
    {
        world.Provider.ReleaseRequests();
        using var response = await world.BackgroundSync!.WaitAsync(HoldTimeout);
        Assert.True(response.IsSuccessStatusCode, $"La synchronisation de l'instance {instance} a échoué.");
        Assert.Single(await world.RunsAsync(), run => run.Status.ToString() == status);
    }

    [Then("l'historique contient un seul run {string} pour le créneau du jour")]
    public async Task SingleRunForSlot(string status) =>
        Assert.Single(await world.RunsAsync(), run => run.Status.ToString() == status);

    [Then("l'historique contient un run {string} en échec bloqué par le garde-fou")]
    public async Task HistoryHasGuardBlockedRun(string trigger)
    {
        var last = await world.LastRunAsync();
        Assert.Equal((trigger, SyncRunStatus.Failed, SyncFailureReason.GuardTriggered),
            (last.Trigger.ToString(), last.Status, last.FailureReason!.Value));
    }

    [Then("le détail de l'erreur indique {int} produits concernés et la possibilité de forcer")]
    public void ErrorDetailsGuard(int count)
    {
        var problem = http.LastJson();
        Assert.Equal(count, problem.GetProperty("wouldDiscontinue").GetInt32());
        Assert.Contains("force", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    // --- Signalement (CAT-R12) ---

    [Then("un log d'erreur {string} est émis avec {int} produits actifs et {int} produits qui seraient retirés")]
    public void GuardLogEmitted(string eventName, int active, int wouldDiscontinue)
    {
        var record = FindErrorLog(eventName);
        Assert.Equal(active.ToString(CultureInfo.InvariantCulture), record.GetStructuredStateValue("ActiveProducts"));
        Assert.Equal(wouldDiscontinue.ToString(CultureInfo.InvariantCulture), record.GetStructuredStateValue("WouldDiscontinue"));
    }

    [Then("un log d'erreur {string} est émis avec la cause de l'échec")]
    public void FailureLogEmitted(string eventName) =>
        Assert.False(string.IsNullOrWhiteSpace(FindErrorLog(eventName).GetStructuredStateValue("Cause")));

    [Then("l'état indique qu'une action est requise pour la raison {string}")]
    public void ActionRequired(string reason)
    {
        var status = http.LastJson();
        Assert.True(status.GetProperty("actionRequired").GetBoolean(), http.LastBody);
        Assert.Contains(reason, status.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Then("l'action conseillée invite à vérifier le fournisseur avant de forcer la synchronisation")]
    public void RecommendedActionGuard()
    {
        var action = http.LastJson().GetProperty("recommendedAction").GetString();
        Assert.Contains("Vérifier le fournisseur avant de forcer", action, StringComparison.Ordinal);
        Assert.Contains("force=true", action, StringComparison.Ordinal);
    }

    [Then("l'état indique le nombre d'échecs consécutifs et la prochaine tentative planifiée")]
    public void StatusShowsFailuresAndNextAttempt()
    {
        var status = http.LastJson();
        Assert.True(status.GetProperty("consecutiveFailures").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.String, status.GetProperty("nextScheduledAttemptAt").ValueKind);
    }

    [Then("l'état indique qu'aucune action n'est requise")]
    public void NoActionRequired() => Assert.False(http.LastJson().GetProperty("actionRequired").GetBoolean(), http.LastBody);

    [Then("l'âge de la dernière synchronisation réussie est de {int} h {int} min")]
    public void LastSuccessAge(int hours, int minutes)
    {
        var age = TimeSpan.Parse(http.LastJson().GetProperty("lastSuccess").GetProperty("age").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(new TimeSpan(hours, minutes, 0), age);
    }

    // --- Outils ---

    private Microsoft.Extensions.Logging.Testing.FakeLogRecord FindErrorLog(string eventName) =>
        Assert.Single(world.Cluster.Doubles.Logs.GetSnapshot(), record => record.Id.Name == eventName && record.Level == LogLevel.Error);

    private async Task InsertFailedScheduledRunAsync(DateTimeOffset at)
    {
        var run = SyncRun.Start(SyncTrigger.Scheduled, "instance-1", forced: false, at);
        run.Fail(SyncFailureReason.ProviderError, "Fournisseur indisponible (contexte de test).", at);
        await world.InsertRunAsync(run);
    }

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
