namespace RouteJumper.Models
{
    /// <summary>
    /// One autocomplete suggestion from Spansh's system-name search (the Spansh menu -
    /// see SpanshRouteService.SearchSystemNamesAsync). <see cref="Id"/> is whatever value Spansh's
    /// own API expects back as the "source"/"destination" form field when requesting a route - not
    /// necessarily the same value as <see cref="Id64"/> (the system's real, stable Elite Dangerous
    /// system address), though in practice they may well be the same number.
    /// </summary>
    public readonly record struct SpanshSystemSuggestion(string Id, long? Id64, string Name);
}
