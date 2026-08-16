using System.Collections.ObjectModel;
using System.Windows.Threading;
using RouteJumper.Common;
using RouteJumper.Models;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// One "type a system name, pick from live Spansh suggestions" field - shared by the Spansh
    /// import dialog's (Integrations &gt; Spansh) Source and Destination fields. 200ms after the
    /// user last typed a character, issues a search via the injected <c>search</c> delegate
    /// (SpanshRouteService.SearchSystemNamesAsync in production) and populates
    /// <see cref="Suggestions"/>. <see cref="Selected"/> only ever becomes non-null via an actual
    /// pick from those suggestions - typing further after a pick clears it again - since Calculate
    /// needs a resolved system id, not just typed text (Spansh's route endpoint takes ids, not
    /// names).
    /// </summary>
    public class SpanshSystemPickerViewModel : ObservableObject
    {
        /// <summary>Fallback for a caller that doesn't specify one (e.g. a test) - matches AppConfigStore's own default for SpanshAutocompleteDebounceMs.</summary>
        private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(250);

        private readonly Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> _search;
        private readonly DispatcherTimer _debounceTimer;
        private CancellationTokenSource? _searchCts;

        private string _query = string.Empty;
        private SpanshSystemSuggestion? _selected;
        private bool _isDropDownOpen;
        private bool _suppressNextQueryChangeReset;

        /// <summary><paramref name="debounceDelay"/> defaults to DefaultDebounceDelay if not given - production (SpanshImportViewModel) always passes AppConfigStore.SpanshAutocompleteDebounceMs explicitly, so it can be tweaked via routejumper.conf without a rebuild.</summary>
        public SpanshSystemPickerViewModel(
            Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> search,
            TimeSpan? debounceDelay = null)
        {
            _search = search;
            Suggestions = new ObservableCollection<SpanshSystemSuggestion>();

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = debounceDelay ?? DefaultDebounceDelay };
            _debounceTimer.Tick += async (_, _) =>
            {
                _debounceTimer.Stop();
                await RunSearchAsync();
            };
        }

        public ObservableCollection<SpanshSystemSuggestion> Suggestions { get; }

        /// <summary>The field's own typed/displayed text - two-way bound to the view's editable ComboBox.</summary>
        public string Query
        {
            get => _query;
            set
            {
                // Set by the Selected setter below, immediately before it assigns Query to match
                // the picked suggestion's own name - that assignment must not itself be treated as
                // "the user typed something new", which would otherwise immediately clear the
                // selection this same setter is in the middle of applying. Consumed unconditionally
                // here, before SetProperty's own no-op-on-unchanged-value short circuit below can
                // skip past it - the picked suggestion's name often already matches what's
                // currently typed (SetProperty then returns false), and leaving the flag armed in
                // that case would incorrectly suppress the very next real keystroke instead.
                var suppressThisChange = _suppressNextQueryChangeReset;
                _suppressNextQueryChangeReset = false;

                if (!SetProperty(ref _query, value) || suppressThisChange)
                {
                    return;
                }

                if (_selected != null)
                {
                    _selected = null;
                    OnPropertyChanged(nameof(Selected));
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }

                _debounceTimer.Stop();
                _searchCts?.Cancel();

                if (string.IsNullOrWhiteSpace(value))
                {
                    Suggestions.Clear();
                    IsDropDownOpen = false;
                    return;
                }

                _debounceTimer.Start();
            }
        }

        /// <summary>Non-null only once the user has actually picked a suggestion (see this class's own doc comment) - what Calculate posts as the source/destination id.</summary>
        public SpanshSystemSuggestion? Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value))
                {
                    return;
                }

                if (value is { } suggestion)
                {
                    // Deliberately does NOT clear Suggestions here: the just-picked item is still
                    // the ComboBox's own SelectedItem at this point (we're inside the callback the
                    // SelectedItem TwoWay binding just invoked), so clearing its ItemsSource out
                    // from under it makes WPF's Selector notice the selected item no longer exists
                    // and immediately reset SelectedItem back to null - which flows straight back
                    // through that same binding and clobbers the selection this setter is in the
                    // middle of applying. Leaving stale suggestions around is harmless: the next
                    // real search (Query's own setter, on further typing) replaces them outright.
                    _suppressNextQueryChangeReset = true;
                    Query = suggestion.Name;
                }

                IsDropDownOpen = false;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsDropDownOpen
        {
            get => _isDropDownOpen;
            set => SetProperty(ref _isDropDownOpen, value);
        }

        /// <summary>Raised whenever Selected changes (a pick, or cleared by further typing) - lets SpanshImportViewModel re-evaluate Calculate's own CanExecute.</summary>
        public event EventHandler? SelectionChanged;

        private async Task RunSearchAsync()
        {
            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;

            IReadOnlyList<SpanshSystemSuggestion> results;
            try
            {
                results = await _search(Query, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                // Superseded by newer input before this search resolved - a stale result must
                // never clobber whatever the (now different) query's own, more recent search is
                // about to populate.
                return;
            }

            Suggestions.Clear();
            foreach (var result in results)
            {
                Suggestions.Add(result);
            }

            IsDropDownOpen = Suggestions.Count > 0;
        }
    }
}
