using RouteJumper.Models;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class SpanshImportViewModelTests
    {
        // ===================== Capitalize (job status wording, e.g. "queued" -> "Queued") =====================

        [Theory]
        [InlineData("queued", "Queued")]
        [InlineData("running", "Running")]
        [InlineData("Already Capitalized", "Already Capitalized")]
        [InlineData("a", "A")]
        [InlineData("", "")]
        public void Capitalize_UppercasesOnlyFirstCharacter(string input, string expected)
        {
            Assert.Equal(expected, SpanshImportViewModel.Capitalize(input));
        }

        // ===================== CreateCachingSearch (Integrations > Spansh, "maintain a cache of
        // queried terms whilst the modal is open and do not rerun queries") =====================

        [Fact]
        public async Task CreateCachingSearch_SameQueryTwice_OnlyCallsUnderlyingSearchOnce()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(new[] { new SpanshSystemSuggestion("1", 1, q) });
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            var first = await cached("Sol", CancellationToken.None);
            var second = await cached("Sol", CancellationToken.None);

            Assert.Equal(1, callCount);
            Assert.Equal(first, second);
        }

        [Fact]
        public async Task CreateCachingSearch_SameQueryDifferentCase_TreatedAsSameCacheEntry()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>());
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await cached("Sol", CancellationToken.None);
            await cached("sol", CancellationToken.None);
            await cached("SOL", CancellationToken.None);

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task CreateCachingSearch_DifferentQueries_CallsUnderlyingForEach()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>());
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await cached("Sol", CancellationToken.None);
            await cached("Deciat", CancellationToken.None);
            await cached("Sola", CancellationToken.None);

            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task CreateCachingSearch_SharedAcrossTwoCallers_SecondCallerHitsSameCache()
        {
            // Mirrors how SpanshImportViewModel wires one cached delegate shared by both the
            // Source and Destination pickers - searching "Sol" in one field must not force a
            // second network round trip when the other field searches "Sol" too.
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(new[] { new SpanshSystemSuggestion("1", 1, q) });
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await cached("Sol", CancellationToken.None); // "Source" field
            await cached("Sol", CancellationToken.None); // "Destination" field

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task CreateCachingSearch_ThrowingSearch_NotCached_RetriedNextTime()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new OperationCanceledException();
                }

                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>());
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await Assert.ThrowsAsync<OperationCanceledException>(() => cached("Sol", CancellationToken.None));
            await cached("Sol", CancellationToken.None);

            Assert.Equal(2, callCount);
        }
    }
}
