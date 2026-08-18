namespace RouteJumper.Models
{
    /// <summary>Result of one poll of a Spansh route job - see SpanshRouteService.GetJobResultAsync.</summary>
    public enum SpanshJobState
    {
        /// <summary>Not yet "completed" (e.g. "queued"/"running") - keep polling.</summary>
        Pending,

        /// <summary>"completed" and the nested result's own status was "ok" - Jumps is populated.</summary>
        Completed,

        /// <summary>Either a transport failure, or "completed" with a nested status other than "ok".</summary>
        Failed
    }

    /// <summary>
    /// One poll's outcome from Spansh's /api/results/{job} endpoint (the Spansh menu).
    /// </summary>
    public sealed class SpanshRouteJobStatus
    {
        private SpanshRouteJobStatus(SpanshJobState state, string? statusText, string? failureReason, IReadOnlyList<SpanshRouteJump> jumps)
        {
            State = state;
            StatusText = statusText;
            FailureReason = failureReason;
            Jumps = jumps;
        }

        public SpanshJobState State { get; }

        /// <summary>The job's own raw top-level status word (e.g. "queued", "running") while Pending.</summary>
        public string? StatusText { get; }

        /// <summary>A human-readable reason, only set when State is Failed.</summary>
        public string? FailureReason { get; }

        /// <summary>Populated only when State is Completed.</summary>
        public IReadOnlyList<SpanshRouteJump> Jumps { get; }

        public static SpanshRouteJobStatus Pending(string statusText) =>
            new(SpanshJobState.Pending, statusText, null, Array.Empty<SpanshRouteJump>());

        public static SpanshRouteJobStatus Completed(IReadOnlyList<SpanshRouteJump> jumps) =>
            new(SpanshJobState.Completed, "completed", null, jumps);

        public static SpanshRouteJobStatus Failed(string reason) =>
            new(SpanshJobState.Failed, null, reason, Array.Empty<SpanshRouteJump>());
    }
}
