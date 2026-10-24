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

            // UI-9: el medidor pintaba con RGB fijo en este archivo, asi que el tema no mandaba
            // sobre el VU y en claro los LED apagados habrian sido cuatro trozos de carbon. La
            // paleta ahora se resuelve contra el diccionario activo (Brush.Vu.* de Tokens) y el
            // control se repinta cuando el ThemeService cambia el hueco del tema.
            ThemeService.ThemeChanged += (_, _) => UpdateSegments();

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

        /// <summary>
        /// El medidor expone su nivel por UIA (patrones Value y RangeValue). Hace falta porque un
        /// UserControl normal no deleva nada legible desde fuera: los VU de la consola no se
        /// podian verificar mas que con una captura de pantalla, y una captura no demuestra que la
        /// barra se mueva con el nivel —de hecho tapo el primer bug de UI-7, que el binding del
        /// master apuntaba a un ViewModel equivocado y la barra pintaba siempre a cero.
        /// </summary>
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
            => new LedVuMeterAutomationPeer(this);

        private sealed class LedVuMeterAutomationPeer : System.Windows.Automation.Peers.FrameworkElementAutomationPeer,
            System.Windows.Automation.Provider.IValueProvider, System.Windows.Automation.Provider.IRangeValueProvider
        {
            private readonly LedVuMeterControl _meter;

            public LedVuMeterAutomationPeer(LedVuMeterControl meter) : base(meter)
            {
                _meter = meter;
            }

            public override object GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface)
            {
                if (patternInterface == System.Windows.Automation.Peers.PatternInterface.Value ||
                    patternInterface == System.Windows.Automation.Peers.PatternInterface.RangeValue)
                {
                    return this;
                }

                return base.GetPattern(patternInterface);
            }

            protected override string GetClassNameCore() => "LedVuMeter";

            // IValueProvider
            bool System.Windows.Automation.Provider.IValueProvider.IsReadOnly => true;
            string System.Windows.Automation.Provider.IValueProvider.Value => _meter.Value.ToString("0.0");
            void System.Windows.Automation.Provider.IValueProvider.SetValue(string value)
                => throw new System.Windows.Automation.ElementNotEnabledException("Un VU no se escribe: mide.");

            // IRangeValueProvider
            double System.Windows.Automation.Provider.IRangeValueProvider.Value => _meter.Value;
            double System.Windows.Automation.Provider.IRangeValueProvider.Minimum => 0d;
            double System.Windows.Automation.Provider.IRangeValueProvider.Maximum => 100d;
            double System.Windows.Automation.Provider.IRangeValueProvider.SmallChange => 1d;
            double System.Windows.Automation.Provider.IRangeValueProvider.LargeChange => 10d;
            bool System.Windows.Automation.Provider.IRangeValueProvider.IsReadOnly => true;
            void System.Windows.Automation.Provider.IRangeValueProvider.SetValue(double value)
                => throw new System.Windows.Automation.ElementNotEnabledException("Un VU no se escribe: mide.");
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
            var palette = VuPalette.Load();
            var activeSegments = (int)Math.Round((Math.Clamp(Value, 0d, 100d) / 100d) * SegmentCount);
            var segmentList = new List<Border>();

            _segmentsGrid.Orientation = Orientation == Orientation.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical;

            for (var index = 0; index < SegmentCount; index++)
            {
                var segmentLevel = ((index + 1) / (double)SegmentCount) * 100d;
                var isActive = index < activeSegments;
                var fill = GetSegmentBrush(palette, segmentLevel, isActive);
                var segment = new Border
                {
                    Background = fill,
                    CornerRadius = new CornerRadius(2),
                    Margin = Orientation == Orientation.Horizontal
                        ? new Thickness(1.2, 0, 1.2, 0)
                        : new Thickness(0, 1.2, 0, 1.2),
                    Opacity = isActive ? 1.0 : 0.18,
                    BorderBrush = palette.Border,
                    BorderThickness = new Thickness(0.7),
                    MinWidth = Orientation == Orientation.Horizontal ? 8 : 16,
                    MinHeight = Orientation == Orientation.Horizontal ? 18 : 8,
                    MaxWidth = Orientation == Orientation.Horizontal ? 14 : 20,
                    MaxHeight = Orientation == Orientation.Horizontal ? 20 : 16,
                    Effect = isActive ? new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = ((SolidColorBrush)fill).Color,
                        ShadowDepth = 0,
                        BlurRadius = palette.GlowBlur,
                        Opacity = palette.GlowOpacity
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

        /// <summary>
        /// Los cinco colores y el resplandor del LED, leidos del diccionario del tema activo.
        /// Cada valor tiene su default en Tokens.xaml; si aun no hay aplicacion (el control se
        /// construye durante el parseo de la ventana) se cae al RGB historico, que es exactamente
        /// lo que el medidor pintaba antes de UI-9: asi el control no depende del orden de arranque.
        /// </summary>
        private sealed class VuPalette
        {
            public required SolidColorBrush Low { get; init; }
            public required SolidColorBrush Mid { get; init; }
            public required SolidColorBrush High { get; init; }
            public required SolidColorBrush Off { get; init; }
            public required SolidColorBrush Border { get; init; }
            public double GlowOpacity { get; init; } = 0.7;
            public double GlowBlur { get; init; } = 9;

            public static VuPalette Load()
            {
                var app = Application.Current;
                return new VuPalette
                {
                    Low = BrushOf(app, "Brush.Vu.Low", Color.FromRgb(34, 197, 94)),
                    Mid = BrushOf(app, "Brush.Vu.Mid", Color.FromRgb(250, 204, 21)),
                    High = BrushOf(app, "Brush.Vu.High", Color.FromRgb(239, 68, 68)),
                    Off = BrushOf(app, "Brush.Vu.Off", Color.FromRgb(34, 41, 52)),
                    Border = BrushOf(app, "Brush.Vu.Border", Color.FromRgb(41, 49, 60)),
                    GlowOpacity = DoubleOf(app, "Sys.Vu.GlowOpacity", 0.7),
                    GlowBlur = DoubleOf(app, "Sys.Vu.GlowBlur", 9),
                };
            }

            private static SolidColorBrush BrushOf(Application? app, string key, Color fallback)
            {
                if (app?.TryFindResource(key) is SolidColorBrush brush) return brush;
                var fresh = new SolidColorBrush(fallback);
                fresh.Freeze();
                return fresh;
            }

            private static double DoubleOf(Application? app, string key, double fallback)
                => app?.TryFindResource(key) is double value ? value : fallback;
        }

        private static Brush GetSegmentBrush(VuPalette palette, double relativeLevel, bool isActive)
        {
            if (!isActive)
            {
                return palette.Off;
            }

            if (relativeLevel < 60d)
            {
                return palette.Low;
            }

            if (relativeLevel < 90d)
            {
                return palette.Mid;
            }

            return palette.High;
        }
    }
}
