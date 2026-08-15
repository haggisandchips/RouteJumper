using System.Diagnostics;
using System.Net.Http;

namespace RouteJumper.Services.Logging
{
    /// <summary>
    /// Logs every outbound HTTP request/response (or failure) this app makes through it - EDSM's
    /// coordinates/star-type lookups (EdsmStarSystemLookupService), currently the app's only
    /// direct HttpClient usage (see SPEC.md's Network section). Velopack's own internal
    /// update-check HTTP calls aren't visible here - Velopack owns its own HttpClient internally
    /// with no handler-injection seam - so those are covered instead by the Info/Warn calls
    /// already around UpdateService's own check.
    /// </summary>
    internal sealed class LoggingHttpMessageHandler : DelegatingHandler
    {
        private const string Category = "Http";

        public LoggingHttpMessageHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            Log.Info(Category, $"-> {request.Method} {request.RequestUri}");

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                stopwatch.Stop();
                Log.Info(Category, $"<- {(int)response.StatusCode} {response.StatusCode} {request.Method} {request.RequestUri} ({stopwatch.ElapsedMilliseconds}ms)");
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log.Warn(Category, $"x {request.Method} {request.RequestUri} failed after {stopwatch.ElapsedMilliseconds}ms", ex);
                throw;
            }
        }
    }
}
