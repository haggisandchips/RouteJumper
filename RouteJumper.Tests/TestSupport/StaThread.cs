namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Clipboard access (System.Windows.Clipboard) requires an STA thread. xUnit test methods run
    /// MTA by default, so anything that touches the clipboard runs its body on a dedicated STA
    /// thread via this helper instead.
    /// </summary>
    internal static class StaThread
    {
        public static void Run(Action action)
        {
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (captured != null)
            {
                throw captured;
            }
        }
    }
}
