using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace quasar_radio
{
    public class LedVuMeterControl : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(LedVuMeterControl),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnValueChanged));

        public static readonly DependencyProperty SegmentCountProperty =
            DependencyProperty.Register(nameof(SegmentCount), typeof(int), typeof(LedVuMeterControl),
                new PropertyMetadata(20, OnAppearanceChanged));

        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(LedVuMeterControl),
                new PropertyMetadata(Orientation.Horizontal, OnAppearanceChanged));

        private readonly StackPanel _segmentsGrid;

        public LedVuMeterControl()
        {
            _segmentsGrid = new StackPanel
            {
                Margin = new Thickness(0),
                Orientation = Orientation.Horizontal,
                SnapsToDevicePixels = true
            };

            Content = _segmentsGrid;
            UpdateSegments();
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, Math.Clamp(value, 0d, 100d));
        }

        public int SegmentCount
        {
            get => (int)GetValue(SegmentCountProperty);
            set => SetValue(SegmentCountProperty, Math.Clamp(value, 6, 36));
        }

        public Orientation Orientation
        {
            get => (Orientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LedVuMeterControl meter)
            {
                meter.UpdateSegments();
            }
        }

        private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LedVuMeterControl meter)
            {
                meter.UpdateSegments();
            }
        }

        private void UpdateSegments()
        {
            var activeSegments = (int)Math.Round((Math.Clamp(Value, 0d, 100d) / 100d) * SegmentCount);
            var segmentList = new List<Border>();

            _segmentsGrid.Orientation = Orientation == Orientation.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical;

            for (var index = 0; index < SegmentCount; index++)
            {
                var segmentLevel = ((index + 1) / (double)SegmentCount) * 100d;
                var isActive = index < activeSegments;
                var fill = GetSegmentBrush(segmentLevel, isActive);
                var segment = new Border
                {
                    Background = fill,
                    CornerRadius = new CornerRadius(2),
                    Margin = Orientation == Orientation.Horizontal
                        ? new Thickness(1.2, 0, 1.2, 0)
                        : new Thickness(0, 1.2, 0, 1.2),
                    Opacity = isActive ? 1.0 : 0.18,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(41, 49, 60)),
                    BorderThickness = new Thickness(0.7),
                    MinWidth = Orientation == Orientation.Horizontal ? 8 : 16,
                    MinHeight = Orientation == Orientation.Horizontal ? 18 : 8,
                    MaxWidth = Orientation == Orientation.Horizontal ? 14 : 20,
                    MaxHeight = Orientation == Orientation.Horizontal ? 20 : 16,
                    Effect = isActive ? new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = ((SolidColorBrush)fill).Color,
                        ShadowDepth = 0,
                        BlurRadius = 9,
                        Opacity = 0.7
                    } : null
                };

                segmentList.Add(segment);
            }

            _segmentsGrid.Children.Clear();
            foreach (var segment in segmentList)
            {
                _segmentsGrid.Children.Add(segment);
            }
        }

        private static Brush GetSegmentBrush(double relativeLevel, bool isActive)
        {
            if (!isActive)
            {
                return new SolidColorBrush(Color.FromRgb(34, 41, 52));
            }

            if (relativeLevel < 60d)
            {
                return new SolidColorBrush(Color.FromRgb(34, 197, 94));
            }

            if (relativeLevel < 90d)
            {
                return new SolidColorBrush(Color.FromRgb(250, 204, 21));
            }

            return new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
    }
}
