using System;
using System.Collections.Generic;
using System.Windows;

namespace quasar_radio
{
    public partial class TrackEditorDialog : Window
    {
        private readonly List<DateTime> _tapTimes = new List<DateTime>();

        public TrackEditorDialog(TrackItem track)
        {
            InitializeComponent();
            DataContext = track;
        }

        private void OnTapBpmClick(object sender, RoutedEventArgs e)
        {
            DateTime now = DateTime.Now;
            if (_tapTimes.Count > 0 && (now - _tapTimes[^1]).TotalSeconds > 2.5)
            {
                _tapTimes.Clear();
            }

            _tapTimes.Add(now);

            if (_tapTimes.Count > 1 && DataContext is TrackItem track)
            {
                double totalSeconds = (_tapTimes[^1] - _tapTimes[0]).TotalSeconds;
                double bpm = ((_tapTimes.Count - 1) / totalSeconds) * 60.0;
                track.Bpm = Math.Round(bpm, 1);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
