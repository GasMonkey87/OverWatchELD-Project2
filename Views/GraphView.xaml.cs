using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Views
{
    public partial class GraphView : UserControl
    {
        private double _zoom = 1.0;

        public GraphView()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyZoomSafe();
            RedrawSafe();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // detach events/timers here if added later
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            RedrawSafe();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoom = Math.Min(3.0, _zoom + 0.1);
            ApplyZoomSafe();
            RedrawSafe();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoom = Math.Max(0.5, _zoom - 0.1);
            ApplyZoomSafe();
            RedrawSafe();
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            ApplyZoomSafe();
            RedrawSafe();
        }

        private void ApplyZoomSafe()
        {
            if (FindName("GraphRoot") is FrameworkElement root)
            {
                root.RenderTransformOrigin = new Point(0.5, 0.5);
                root.RenderTransform = new ScaleTransform(_zoom, _zoom);
            }
        }

        private void RedrawSafe()
        {
            if (FindName("GraphCanvas") is UIElement canvasEl)
                canvasEl.InvalidateVisual();

            if (FindName("GraphSurface") is UIElement surfaceEl)
                surfaceEl.InvalidateVisual();

            InvalidateVisual();
            UpdateLayout();
        }
    }
}
