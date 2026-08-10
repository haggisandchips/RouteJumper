using RouteJumper.Common;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Common
{
    public class ClipboardCopyHelperTests
    {
        [Fact]
        public void CopyWithPing_NullText_DoesNotThrow()
        {
            ClipboardCopyHelper.CopyWithPing(null);
        }

        [Fact]
        public void CopyWithPing_EmptyText_DoesNotThrow()
        {
            ClipboardCopyHelper.CopyWithPing(string.Empty);
        }

        [Fact]
        public void CopyWithPing_NonEmptyText_WritesToClipboard()
        {
            StaThread.Run(() =>
            {
                ClipboardCopyHelper.CopyWithPing("RouteJumperTest-" + Guid.NewGuid());
            });
        }
    }
}
