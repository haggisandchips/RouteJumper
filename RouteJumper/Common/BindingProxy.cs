using System.Windows;

namespace RouteJumper.Common
{
    /// <summary>
    /// Standard WPF workaround for binding a <c>DataGridColumn</c> (Jumps/Refuel/Inject/Neutron in
    /// RouteView.xaml) to something on the page's own DataContext. A DataGridColumn is a plain
    /// DependencyObject, not a FrameworkElement - it sits outside the visual/logical tree entirely,
    /// so it never inherits DataContext. The original implementation bound each column's own
    /// Visibility via ElementName back to the DataGrid, which is a documented anti-pattern for
    /// exactly this scenario - confirmed live it left every one of those four columns always
    /// Visible (their DP default) regardless of RouteType, since ElementName resolution still
    /// relies on the same tree walk a DataGridColumn has no place in.
    ///
    /// A Freezable does participate in binding inheritance even while sitting in a
    /// ResourceDictionary (a documented WPF exception to the "not in the tree" rule), so placing
    /// one in UserControl.Resources with <c>Data="{Binding}"</c> gives every DataGridColumn a
    /// stable path back to the page's own DataContext: <c>{Binding Data.X, Source={StaticResource
    /// Proxy}}</c>.
    /// </summary>
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
    }
}
