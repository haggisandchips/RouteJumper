using System.Net;
using System.Net.Http;
using RouteJumper.Services.Companion;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services.Companion
{
    public class CompanionSessionPublisherTests
    {
        private const string ProjectId = "test-project";
        private const int DefaultRetentionHours = 1;
        private const int AbsoluteMaxAgeHours = 72;

        private static (CompanionSessionPublisher Publisher, FakeHttpMessageHandler Handler, CompanionSessionStore Store) Create(
            string tempDir, int retentionHours = DefaultRetentionHours)
        {
            var handler = new FakeHttpMessageHandler();
            var store = new CompanionSessionStore(tempDir);
            var publisher = new CompanionSessionPublisher(ProjectId, new HttpClient(handler), () => retentionHours, store);
            return (publisher, handler, store);
        }

        private static async Task WaitForRequestCountAsync(FakeHttpMessageHandler handler, int count)
        {
            for (var i = 0; i < 60 && handler.RequestedUrls.Count < count; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(handler.RequestedUrls.Count >= count, $"Expected at least {count} request(s), got {handler.RequestedUrls.Count}.");
        }

        // ===================== StartSessionAsync / PublishEvent =====================

        [Fact]
        public async Task StartSessionAsync_PatchesHeaderDocToExpectedUrlWithExpectedFields()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);

            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            Assert.NotNull(sessionId);
            Assert.Equal(sessionId, publisher.CurrentSessionId);

            var url = Assert.Single(handler.RequestedUrls);
            Assert.Equal(
                $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/sessions/{sessionId}",
                url);
            Assert.Equal("PATCH", Assert.Single(handler.RequestedMethods));

            var body = Assert.Single(handler.RequestedBodies);
            Assert.Contains("\"startSystem\"", body);
            Assert.Contains("Sol", body);
            Assert.Contains("Colonia", body);
            Assert.Contains("\"status\"", body);
            Assert.Contains("active", body);
        }

        [Fact]
        public async Task StartSessionAsync_SameRouteTwice_ReturnsTheSameSessionId()
        {
            // The whole point of a deterministic id (§13, per the user's own request to stop the
            // QR code/link changing on every engage): re-engaging on an unchanged route within the
            // same app run must resolve to the same session doc.
            using var dir = new TempDirectory();
            var (publisher, _, _) = Create(dir.Path);

            var first = await publisher.StartSessionAsync(new[] { "Sol", "Deciat", "Colonia" });
            publisher.EndSession(panicked: false);
            var second = await publisher.StartSessionAsync(new[] { "Sol", "Deciat", "Colonia" });

            Assert.Equal(first, second);
        }

        [Fact]
        public async Task StartSessionAsync_DifferentRoute_ReturnsADifferentSessionId()
        {
            using var dir = new TempDirectory();
            var (publisher, _, _) = Create(dir.Path);

            var first = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });
            publisher.EndSession(panicked: false);
            var second = await publisher.StartSessionAsync(new[] { "Sol", "Deciat", "Colonia" });

            Assert.NotEqual(first, second);
        }

        [Fact]
        public async Task StartSessionAsync_DifferentPublisherInstance_ReturnsADifferentSessionIdForTheSameRoute()
        {
            // Simulates an app restart (a fresh appInstanceId) - the same route text should still
            // get a fresh session, never picking up a previous run's stale/deleted doc.
            using var dir = new TempDirectory();
            var (publisherA, _, _) = Create(dir.Path);
            var (publisherB, _, _) = Create(dir.Path);

            var idFromA = await publisherA.StartSessionAsync(new[] { "Sol", "Colonia" });
            var idFromB = await publisherB.StartSessionAsync(new[] { "Sol", "Colonia" });

            Assert.NotEqual(idFromA, idFromB);
        }

        [Fact]
        public async Task StartSessionAsync_ReEngagingAfterEndSession_ReactivatesStatusToActive()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);
            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });
            publisher.EndSession(panicked: true);
            await WaitForRequestCountAsync(handler, 2);

            await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            var reactivateBody = handler.RequestedBodies[2];
            Assert.Equal(sessionId, publisher.CurrentSessionId);
            Assert.Contains("\"status\"", reactivateBody);
            Assert.Contains("active", reactivateBody);
        }

        [Fact]
        public async Task StartSessionAsync_RecordsSessionLocallyWithTheAbsoluteMaxAgeBackstop()
        {
            // Covers a session that's later abandoned (app crash, force-quit, EndSession never
            // called) - without this, it would never be recorded as due for deletion at all.
            using var dir = new TempDirectory();
            var (publisher, _, store) = Create(dir.Path);

            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            Assert.DoesNotContain(sessionId!.Value, store.GetSessionsDueForDeletion(DateTime.UtcNow));
            Assert.Contains(sessionId.Value, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddHours(AbsoluteMaxAgeHours + 1)));
        }

        [Fact]
        public async Task StartSessionAsync_RequestFails_ReturnsNullAndDoesNotThrow()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);
            handler.Respond = _ => (HttpStatusCode.InternalServerError, string.Empty);

            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            Assert.Null(sessionId);
            Assert.Null(publisher.CurrentSessionId);
        }

        [Fact]
        public async Task PublishEvent_NoSessionStarted_DoesNothing()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);

            publisher.PublishEvent(CompanionEventKind.Plotted, "Sol", "Jump plotted to Sol");
            await Task.Delay(50);

            Assert.Empty(handler.RequestedUrls);
        }

        [Fact]
        public async Task PublishEvent_SessionStarted_PostsEventToExpectedSubcollectionUrl()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);
            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            publisher.PublishEvent(CompanionEventKind.Arrived, "Deciat", "Arrived at Deciat");
            await WaitForRequestCountAsync(handler, 2);

            Assert.Equal(
                $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/sessions/{sessionId}/events",
                handler.RequestedUrls[1]);

            var body = handler.RequestedBodies[1];
            Assert.Contains("\"kind\"", body);
            Assert.Contains("arrived", body);
            Assert.Contains("Deciat", body);
        }

        [Fact]
        public async Task PublishEvent_RequestFails_NeverThrows()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);
            await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });
            handler.Respond = _ => (HttpStatusCode.InternalServerError, string.Empty);

            var exception = Record.Exception(() => publisher.PublishEvent(CompanionEventKind.Panic, string.Empty, "boom"));
            await Task.Delay(50);

            Assert.Null(exception);
        }

        // ===================== EndSession =====================

        [Fact]
        public async Task EndSession_MarksStatusAndClearsCurrentSessionId()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);
            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            publisher.EndSession(panicked: true);
            await WaitForRequestCountAsync(handler, 2);

            Assert.Null(publisher.CurrentSessionId);

            var headerPatchUrl = handler.RequestedUrls[1];
            Assert.Equal(
                $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/sessions/{sessionId}?updateMask.fieldPaths=status",
                headerPatchUrl);
            Assert.Contains("panicked", handler.RequestedBodies[1]);
        }

        [Fact]
        public async Task EndSession_RecordsSessionLocallyAsDueOnlyAfterTheRetentionWindow()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path, retentionHours: 48);
            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            publisher.EndSession(panicked: false);
            await WaitForRequestCountAsync(handler, 2);

            Assert.DoesNotContain(sessionId!.Value, store.GetSessionsDueForDeletion(DateTime.UtcNow));
            Assert.Contains(sessionId.Value, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddHours(49)));
        }

        [Fact]
        public async Task EndSession_RetentionHoursConfiguredBeyondTheAbsoluteMax_IsClampedToIt()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path, retentionHours: 500);
            var sessionId = await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });

            publisher.EndSession(panicked: false);
            await WaitForRequestCountAsync(handler, 2);

            // Still due once the fixed 72h backstop has passed - never pushed out to 500h.
            Assert.Contains(sessionId!.Value, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddHours(AbsoluteMaxAgeHours + 1)));
            Assert.DoesNotContain(sessionId.Value, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddHours(AbsoluteMaxAgeHours - 1)));
        }

        [Fact]
        public async Task EndSession_NoSessionStarted_DoesNothing()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);

            publisher.EndSession(panicked: false);
            await Task.Delay(50);

            Assert.Empty(handler.RequestedUrls);
            Assert.Empty(store.GetSessionsDueForDeletion(DateTime.UtcNow.AddYears(1)));
        }

        [Fact]
        public async Task EndSession_ThenPublishEvent_DoesNotPostAnyFurtherEventRequests()
        {
            // A stale continuation firing after EndSession (e.g. a race with Auto Pilot stopping)
            // must never publish into a session that's already been marked completed/panicked.
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);
            await publisher.StartSessionAsync(new[] { "Sol", "Colonia" });
            publisher.EndSession(panicked: false);
            await WaitForRequestCountAsync(handler, 2);
            var countAfterEnd = handler.RequestedUrls.Count;

            publisher.PublishEvent(CompanionEventKind.Arrived, "Deciat", "Arrived at Deciat");
            await Task.Delay(50);

            Assert.Equal(countAfterEnd, handler.RequestedUrls.Count);
        }

        // ===================== CleanUpExpiredSessionsAsync =====================

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_NothingRecorded_MakesNoRequests()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, _) = Create(dir.Path);

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.Empty(handler.RequestedUrls);
        }

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_SessionNotYetDue_LeavesItAloneAndMakesNoRequests()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(1));

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.Empty(handler.RequestedUrls);
            Assert.Contains(sessionId, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddHours(2)));
        }

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_SessionDue_DeletesEventsThenHeaderAndRemovesFromLocalStore()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));

            handler.Respond = url => url.Contains("/events?pageSize=1000")
                ? (HttpStatusCode.OK, $$"""
                    {"documents":[
                      {"name":"projects/{{ProjectId}}/databases/(default)/documents/sessions/{{sessionId}}/events/evt1"},
                      {"name":"projects/{{ProjectId}}/databases/(default)/documents/sessions/{{sessionId}}/events/evt2"}
                    ]}
                    """)
                : (HttpStatusCode.OK, "{}");

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.Contains(handler.RequestedUrls, u => u.EndsWith($"/sessions/{sessionId}/events?pageSize=1000"));
            Assert.Contains(handler.RequestedUrls, u => u.EndsWith($"/sessions/{sessionId}/events/evt1"));
            Assert.Contains(handler.RequestedUrls, u => u.EndsWith($"/sessions/{sessionId}/events/evt2"));
            Assert.Contains(handler.RequestedUrls, u => u.EndsWith($"/sessions/{sessionId}"));
            Assert.Empty(store.GetSessionsDueForDeletion(DateTime.UtcNow.AddYears(1)));
        }

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_EventsListRequestFails_LeavesSessionTrackedForRetry()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));
            handler.Respond = _ => (HttpStatusCode.InternalServerError, string.Empty);

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.Contains(sessionId, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddYears(1)));
        }

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_OneEventDeleteFails_NeverDeletesHeaderAndLeavesSessionTracked()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));

            handler.Respond = url => url switch
            {
                _ when url.Contains("/events?pageSize=1000") => (HttpStatusCode.OK, $$"""
                    {"documents":[{"name":"projects/{{ProjectId}}/databases/(default)/documents/sessions/{{sessionId}}/events/evt1"}]}
                    """),
                _ when url.EndsWith("/events/evt1") => (HttpStatusCode.InternalServerError, string.Empty),
                _ => (HttpStatusCode.OK, "{}")
            };

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.DoesNotContain(handler.RequestedUrls, u => u.EndsWith($"/sessions/{sessionId}"));
            Assert.Contains(sessionId, store.GetSessionsDueForDeletion(DateTime.UtcNow.AddYears(1)));
        }

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_HeaderAlreadyGone404_StillCountsAsSuccess()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));

            handler.Respond = url => url.Contains("/events?pageSize=1000")
                ? (HttpStatusCode.OK, """{"documents":[]}""")
                : (HttpStatusCode.NotFound, string.Empty);

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.Empty(store.GetSessionsDueForDeletion(DateTime.UtcNow.AddYears(1)));
        }

        [Fact]
        public async Task CleanUpExpiredSessionsAsync_MultipleDueSessions_AreAllProcessed()
        {
            using var dir = new TempDirectory();
            var (publisher, handler, store) = Create(dir.Path);
            var sessionA = Guid.NewGuid();
            var sessionB = Guid.NewGuid();
            store.RecordPendingDeletion(sessionA, DateTime.UtcNow.AddHours(-1));
            store.RecordPendingDeletion(sessionB, DateTime.UtcNow.AddHours(-1));
            handler.Respond = url => url.Contains("/events?pageSize=1000")
                ? (HttpStatusCode.OK, """{"documents":[]}""")
                : (HttpStatusCode.OK, "{}");

            await publisher.CleanUpExpiredSessionsAsync();

            Assert.Empty(store.GetSessionsDueForDeletion(DateTime.UtcNow.AddYears(1)));
            Assert.Contains(handler.RequestedUrls, u => u.Contains(sessionA.ToString()));
            Assert.Contains(handler.RequestedUrls, u => u.Contains(sessionB.ToString()));
        }
    }
}
