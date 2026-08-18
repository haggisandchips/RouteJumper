using System.Windows;
using RouteJumper.ViewModels;

namespace RouteJumper.Views
{
    /// <summary>Modal dialog (Spansh menu) - see SpanshImportViewModel for the actual search/Calculate/poll logic this just hosts and closes on completion.</summary>
    public partial class SpanshImportWindow : Window
    {
        private readonly SpanshImportViewModel _viewModel;

        /// <summary><paramref name="initialTabIndex"/> selects which tab is showing when the dialog opens (0 = Fleet Carrier, 1 = Neutron Plotter, matching this XAML's own TabControl order) - MainWindow's Spansh menu passes the tab the CMDR actually clicked, rather than always defaulting to the first one.</summary>
        public SpanshImportWindow(SpanshImportViewModel viewModel, int initialTabIndex = 0)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            _viewModel.RouteApplied += OnRouteApplied;
            Closed += OnClosed;
            SpanshTabs.SelectedIndex = initialTabIndex;
            Loaded += (_, _) => (initialTabIndex == 1 ? NeutronSourceBox : SourceBox).FocusQueryBox();
        }

        private void OnRouteApplied(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Close);

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel.RouteApplied -= OnRouteApplied;
            Closed -= OnClosed;
            _viewModel.CancelInFlightWork();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
