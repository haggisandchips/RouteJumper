namespace RouteJumper.Services.Logging
{
    /// <summary>Housekeeping knobs for FileLogSink, read from routejumper.conf (AppConfigStore) - see that class for the defaults (7 days / 10MB per file / 100MB total).</summary>
    public sealed record LogSettings(int RetentionDays, int MaxFileSizeMb, int MaxTotalSizeMb)
    {
        public long MaxFileSizeBytes => (long)Math.Max(1, MaxFileSizeMb) * 1024 * 1024;

        public long MaxTotalSizeBytes => (long)Math.Max(1, MaxTotalSizeMb) * 1024 * 1024;
    }
}
