using RouteJumper.Models;
using Xunit;

namespace RouteJumper.Tests.Models
{
    public class RecordedMacroTests
    {
        [Fact]
        public void Defaults_AreEmptyNotNull()
        {
            var macro = new RecordedMacro();

            Assert.Equal(Guid.Empty, macro.Id);
            Assert.Equal(string.Empty, macro.Name);
            Assert.Equal(string.Empty, macro.ScriptText);
            Assert.Equal(string.Empty, macro.SourceCommanderName);
            Assert.Equal(0, macro.SourceProcessId);
        }
    }
}
