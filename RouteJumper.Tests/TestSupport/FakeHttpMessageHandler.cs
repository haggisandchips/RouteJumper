using System.Net;
using System.Net.Http;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Test double for HttpClient's own transport: records every requested URL and answers with
    /// a response built by <see cref="Respond"/> - the standard seam for testing HttpClient-based
    /// code without a real network call. Used by EdsmStarSystemLookupServiceTests.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = new();

        /// <summary>The request body (e.g. a POST's form-encoded content) for each request in RequestedUrls, in the same order - empty string for a request with no content (e.g. a GET).</summary>
        public List<string> RequestedBodies { get; } = new();

        /// <summary>Called once per request; returns the (status, body) to answer with. Defaults to an empty 200 JSON array.</summary>
        public Func<string, (HttpStatusCode StatusCode, string Body)> Respond { get; set; } =
            _ => (HttpStatusCode.OK, "[]");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // OriginalString (not ToString(), which unescapes characters like %20 back to a
            // literal space for display purposes) - tests assert against the exact wire format.
            var url = request.RequestUri!.OriginalString;
            RequestedUrls.Add(url);
            RequestedBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var (statusCode, body) = Respond(url);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };
            return response;
        }
    }
}
