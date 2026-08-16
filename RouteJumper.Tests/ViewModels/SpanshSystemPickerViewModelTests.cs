using RouteJumper.Models;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class SpanshSystemPickerViewModelTests
    {
        private static SpanshSystemPickerViewModel Create() =>
            new((_, _) => Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>()));

        [Fact]
        public void Selected_SetToSuggestion_DoesNotClearSuggestionsList()
        {
            // Regression guard: the Selected setter used to call Suggestions.Clear() right after
            // assigning the pick - but the just-picked item is still the ComboBox's own
            // SelectedItem at that point (this setter runs *from* that TwoWay binding), so
            // clearing its ItemsSource out from under it made WPF's Selector immediately reset
            // SelectedItem back to null, which flowed straight back through the same binding and
            // silently undid the selection - "the dropdown doesn't work" from the UI's perspective.
            var vm = Create();
            var suggestion = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.Suggestions.Add(suggestion);

            vm.Selected = suggestion;

            Assert.Single(vm.Suggestions);
            Assert.Equal(suggestion, vm.Selected);
        }

        [Fact]
        public void Selected_Set_UpdatesQueryToSuggestionName()
        {
            var vm = Create();

            vm.Selected = new SpanshSystemSuggestion("1", 1, "Deciat");

            Assert.Equal("Deciat", vm.Query);
        }

        [Fact]
        public void Selected_Set_ClosesDropDown()
        {
            var vm = Create();
            vm.IsDropDownOpen = true;

            vm.Selected = new SpanshSystemSuggestion("1", 1, "Deciat");

            Assert.False(vm.IsDropDownOpen);
        }

        [Fact]
        public void Query_TypedAfterASelection_ClearsSelected()
        {
            var vm = Create();
            vm.Selected = new SpanshSystemSuggestion("1", 1, "Deciat");

            vm.Query = "Deciat X";

            Assert.Null(vm.Selected);
        }

        [Fact]
        public void Selected_SuggestionNameMatchesAlreadyTypedQuery_SuppressFlagDoesNotLeakToNextKeystroke()
        {
            // Regression guard: when the picked suggestion's Name is identical to what's already
            // typed, Query's own SetProperty is a no-op - the suppress flag must still be consumed
            // right here, not left armed to incorrectly swallow the *next* real keystroke's own
            // "clear the selection" reaction.
            var vm = Create();
            vm.Query = "Sol";

            vm.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            Assert.NotNull(vm.Selected);

            vm.Query = "Sola";

            Assert.Null(vm.Selected);
        }

        [Fact]
        public void SelectionChanged_RaisedOnPickAndOnClearByTyping()
        {
            var vm = Create();
            var raiseCount = 0;
            vm.SelectionChanged += (_, _) => raiseCount++;

            vm.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.Query = "Sola";

            Assert.Equal(2, raiseCount);
        }
    }
}
