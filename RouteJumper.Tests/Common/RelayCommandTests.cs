using RouteJumper.Common;
using Xunit;

namespace RouteJumper.Tests.Common
{
    public class RelayCommandTests
    {
        [Fact]
        public void Execute_InvokesAction()
        {
            var invoked = false;
            var command = new RelayCommand(() => invoked = true);

            command.Execute(null);

            Assert.True(invoked);
        }

        [Fact]
        public void CanExecute_NoPredicate_IsAlwaysTrue()
        {
            var command = new RelayCommand(() => { });
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public void CanExecute_UsesPredicate()
        {
            var allowed = false;
            var command = new RelayCommand(() => { }, () => allowed);

            Assert.False(command.CanExecute(null));
            allowed = true;
            Assert.True(command.CanExecute(null));
        }

        [Fact]
        public void Constructor_NullExecute_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
        }
    }

    public class RelayCommandOfTTests
    {
        [Fact]
        public void Execute_PassesParameterThrough()
        {
            string? received = null;
            var command = new RelayCommand<string>(value => received = value);

            command.Execute("hello");

            Assert.Equal("hello", received);
        }

        [Fact]
        public void CanExecute_UsesTypedPredicate()
        {
            var command = new RelayCommand<int>(_ => { }, value => value > 0);

            Assert.False(command.CanExecute(-1));
            Assert.True(command.CanExecute(1));
        }

        [Fact]
        public void CanExecute_NoPredicate_IsAlwaysTrue()
        {
            var command = new RelayCommand<string>(_ => { });
            Assert.True(command.CanExecute(null));
        }
    }

    public class AsyncRelayCommandTests
    {
        [Fact]
        public async Task Execute_AwaitsAsyncWork()
        {
            var tcs = new TaskCompletionSource();
            var command = new AsyncRelayCommand(() => tcs.Task);

            command.Execute(null);
            tcs.SetResult();
            await tcs.Task;
        }

        [Fact]
        public void CanExecute_UsesPredicate()
        {
            var busy = true;
            var command = new AsyncRelayCommand(() => Task.CompletedTask, () => !busy);

            Assert.False(command.CanExecute(null));
            busy = false;
            Assert.True(command.CanExecute(null));
        }
    }
}
