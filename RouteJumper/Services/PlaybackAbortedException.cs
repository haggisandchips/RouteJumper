namespace RouteJumper.Services
{
    /// <summary>
    /// Thrown by <see cref="MacroPlayer.PlayAsync"/> when the target window loses focus
    /// mid-playback (SendInput delivers to whatever window currently has focus, not to a
    /// specific HWND, so continuing to send input once focus has moved elsewhere would hit the
    /// wrong window). Kept distinct from <see cref="OperationCanceledException"/> - which
    /// represents a deliberate user Stop - so the caller can show an explanatory message only for
    /// this, unexpected, case.
    /// </summary>
    public sealed class PlaybackAbortedException : Exception
    {
        public PlaybackAbortedException(string message) : base(message)
        {
        }
    }
}
