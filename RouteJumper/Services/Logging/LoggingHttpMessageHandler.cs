using System.Diagnostics;
using System.Net.Http;

namespace RouteJumper.Services.Logging
{
    /// <summary>
    /// Logs every outbound HTTP request/response (or failure) this app makes through it - EDSM's
    /// coordinates/star-type lookups (EdsmStarSystemLookupService), Spansh's route calculations
    /// (SpanshRouteService), the companion site's Firestore REST calls
    /// (CompanionSessionPublisher, category "Companion" - see specs/companion-site.md §13), and
    /// UpdateService's own direct GitHub API call for a release's real date (category "Update") -
    /// the app's only direct HttpClient usages (see specs/non-functional.md's Network section).
    /// Velopack's own internal update-check HTTP
    /// calls aren't visible here - Velopack owns its own HttpClient internally with no
    /// handler-injection seam - so those are covered instead by the Info/Warn calls already around
    /// UpdateService's own check.
    /// </summary>
    internal sealed class LoggingHttpMessageHandler : DelegatingHandler
    {
        private readonly string _category;

        public LoggingHttpMessageHandler(HttpMessageHandler innerHandler, string category = "Http") : base(innerHandler)
        {
            _category = category;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            Log.Info(_category, $"-> {request.Method} {request.RequestUri}");

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                stopwatch.Stop();
                Log.Info(_category, $"<- {(int)response.StatusCode} {response.StatusCode} {request.Method} {request.RequestUri} ({stopwatch.ElapsedMilliseconds}ms)");
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log.Warn(_category, $"x {request.Method} {request.RequestUri} failed after {stopwatch.ElapsedMilliseconds}ms", ex);
                throw;
            }
        }
    }
}
