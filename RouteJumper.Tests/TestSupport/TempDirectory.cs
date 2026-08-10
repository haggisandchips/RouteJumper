using System.IO;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>A fresh scratch directory under the OS temp folder, deleted on Dispose - used to isolate AppSettingsStore/AppConfigStore/journal-file tests from the real per-user AppData location.</summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RouteJumperTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CombinePath(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
