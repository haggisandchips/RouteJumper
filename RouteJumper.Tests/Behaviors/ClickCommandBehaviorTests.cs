using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RouteJumper.Behaviors;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Behaviors
{
    public class ClickCommandBehaviorTests
    {
        private sealed class RecordingCommand : ICommand
        {
            public int ExecuteCount { get; private set; }
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => ExecuteCount++;
        }

        private static MouseButtonEventArgs RaisePreviewMouseLeftButtonUp(UIElement element, UIElement originalSource)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = originalSource
            };
            element.RaiseEvent(args);
            return args;
        }

        [Fact]
        public void Click_DirectlyOnElement_InvokesCommand()
        {
            StaThread.Run(() =>
            {
                var border = new Border();
                var command = new RecordingCommand();
                ClickCommandBehavior.SetCommand(border, command);

                RaisePreviewMouseLeftButtonUp(border, border);

                Assert.Equal(1, command.ExecuteCount);
            });
        }

        [Fact]
        public void Click_OnNestedPlainElement_InvokesCommand()
        {
            StaThread.Run(() =>
            {
                var text = new TextBlock();
                var border = new Border { Child = text };
                var command = new RecordingCommand();
                ClickCommandBehavior.SetCommand(border, command);

                RaisePreviewMouseLeftButtonUp(border, text);

                Assert.Equal(1, command.ExecuteCount);
            });
        }

        [Fact]
        public void Click_OnNestedButton_DoesNotInvokeCommand()
        {
            StaThread.Run(() =>
            {
                var button = new Button();
                var border = new Border { Child = button };
                var command = new RecordingCommand();
                ClickCommandBehavior.SetCommand(border, command);

                RaisePreviewMouseLeftButtonUp(border, button);

                Assert.Equal(0, command.ExecuteCount);
            });
        }

        [Fact]
        public void Click_OnIconInsideNestedButton_DoesNotInvokeCommand()
        {
            StaThread.Run(() =>
            {
                var icon = new TextBlock(); // stand-in for a PackIcon glyph inside the button
                var button = new Button { Content = icon };
                var border = new Border { Child = button };
                var command = new RecordingCommand();
                ClickCommandBehavior.SetCommand(border, command);

                // Content set via the Content property only joins the *logical* tree until a real
                // layout pass runs (normally triggered by being on-screen) - a ContentPresenter
                // doesn't realize its Content into the visual tree until it's actually measured,
                // so ApplyTemplate() on the Button alone isn't enough. Force a full Measure/
                // Arrange here so icon gets a real visual-tree parent chain up through the
                // button, the same as it would once rendered.
                border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                border.Arrange(new Rect(border.DesiredSize));

                RaisePreviewMouseLeftButtonUp(border, icon);

                Assert.Equal(0, command.ExecuteCount);
            });
        }

        [Fact]
        public void SetCommand_ToNull_UnsubscribesHandler()
        {
            StaThread.Run(() =>
            {
                var border = new Border();
                var command = new RecordingCommand();
                ClickCommandBehavior.SetCommand(border, command);
                ClickCommandBehavior.SetCommand(border, null);

                RaisePreviewMouseLeftButtonUp(border, border);

                Assert.Equal(0, command.ExecuteCount);
            });
        }

        [Fact]
        public void GetCommand_And_GetCommandParameter_ReturnSetValues()
        {
            StaThread.Run(() =>
            {
                var border = new Border();
                var command = new RecordingCommand();
                ClickCommandBehavior.SetCommand(border, command);
                ClickCommandBehavior.SetCommandParameter(border, "param");

                Assert.Same(command, ClickCommandBehavior.GetCommand(border));
                Assert.Equal("param", ClickCommandBehavior.GetCommandParameter(border));
            });
        }
    }
}
