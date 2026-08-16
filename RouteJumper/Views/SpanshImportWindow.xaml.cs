using System.Windows;
using RouteJumper.ViewModels;

namespace RouteJumper.Views
{
    /// <summary>Modal dialog (Integrations &gt; Spansh) - see SpanshImportViewModel for the actual search/Calculate/poll logic this just hosts and closes on completion.</summary>
    public partial class SpanshImportWindow : Window
    {
        private readonly SpanshImportViewModel _viewModel;

        public SpanshImportWindow(SpanshImportViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            _viewModel.RouteApplied += OnRouteApplied;
            Closed += OnClosed;
            Loaded += (_, _) => SourceBox.FocusQueryBox();
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
