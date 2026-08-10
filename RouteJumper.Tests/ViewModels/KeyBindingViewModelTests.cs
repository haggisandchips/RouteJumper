using RouteJumper.Models;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class KeyBindingViewModelTests
    {
        [Fact]
        public void ActionName_UsesSpecVocabulary()
        {
            var binding = new KeyBindingViewModel(ControlAction.PrevPanel, "Delete");
            Assert.Equal("PREV_PANEL", binding.ActionName);
        }

        [Fact]
        public void DisplayString_DerivedFromStorageString()
        {
            var binding = new KeyBindingViewModel(ControlAction.Exit, "Back");
            Assert.Equal("Backspace", binding.DisplayString);
        }

        [Fact]
        public void StorageString_Change_AlsoRaisesDisplayStringChanged()
        {
            var binding = new KeyBindingViewModel(ControlAction.Up, "Up");
            var raised = new List<string?>();
            binding.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            binding.StorageString = "Control+Up";

            Assert.Contains(nameof(KeyBindingViewModel.StorageString), raised);
            Assert.Contains(nameof(KeyBindingViewModel.DisplayString), raised);
            Assert.Equal("Ctrl+Up Arrow", binding.DisplayString);
        }

        [Fact]
        public void IsCapturing_DefaultsFalse()
        {
            var binding = new KeyBindingViewModel(ControlAction.Select, "Space");
            Assert.False(binding.IsCapturing);
        }
    }
}
