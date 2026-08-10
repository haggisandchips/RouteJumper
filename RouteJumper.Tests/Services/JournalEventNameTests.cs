using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class JournalEventNameTests
    {
        [Fact]
        public void Extract_ValidLine_ReturnsEventValue()
        {
            var line = "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"CarrierJumpRequest\",\"SystemName\":\"Sol\"}";
            Assert.Equal("CarrierJumpRequest", JournalEventName.Extract(line));
        }

        [Fact]
        public void Extract_EventIsFirstProperty_StillFound()
        {
            var line = "{\"event\":\"Fileheader\"}";
            Assert.Equal("Fileheader", JournalEventName.Extract(line));
        }

        [Fact]
        public void Extract_NoEventMarker_ReturnsNull()
        {
            Assert.Null(JournalEventName.Extract("{\"timestamp\":\"2026-01-01T00:00:00Z\"}"));
        }

        [Fact]
        public void Extract_UnterminatedEventValue_ReturnsNull()
        {
            Assert.Null(JournalEventName.Extract("{\"event\":\"Unterminated"));
        }

        [Fact]
        public void Extract_EmptyLine_ReturnsNull()
        {
            Assert.Null(JournalEventName.Extract(string.Empty));
        }

        [Fact]
        public void Extract_EmptyEventValue_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, JournalEventName.Extract("{\"event\":\"\"}"));
        }
    }
}
