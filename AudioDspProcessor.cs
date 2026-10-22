using System;
using System.Collections.Generic;

namespace quasar_radio
{
    public class AudioDspProcessor : ViewModelBase
    {
        // GANANCIA DE ENTRADA Y SALIDA MASTER
        private float _inputGain = 1.0f;
        public float InputGain
        {
            get => _inputGain;
            set { _inputGain = Math.Clamp(value, 0.0f, 2.0f); OnPropertyChanged(); }
        }

        private float _masterDspGain = 1.0f;
        public float MasterDspGain
        {
            get => _masterDspGain;
            set { _masterDspGain = Math.Clamp(value, 0.0f, 2.0f); OnPropertyChanged(); }
        }

        private bool _isAgcEnabled;
        public bool IsAgcEnabled
        {
            get => _isAgcEnabled;
            set { _isAgcEnabled = value; OnPropertyChanged(); }
        }

        private float _agcTargetDb = -18f;
        public float AgcTargetDb
        {
            get => _agcTargetDb;
            set { _agcTargetDb = Math.Clamp(value, -30f, -6f); OnPropertyChanged(); }
        }

        // ECUALIZADOR 10 BANDAS (-12dB a +12dB)
        private float _band31Hz;
        public float Band31Hz { get => _band31Hz; set { _band31Hz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band62Hz;
        public float Band62Hz { get => _band62Hz; set { _band62Hz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band125Hz;
        public float Band125Hz { get => _band125Hz; set { _band125Hz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band250Hz;
        public float Band250Hz { get => _band250Hz; set { _band250Hz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band500Hz;
        public float Band500Hz { get => _band500Hz; set { _band500Hz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band1kHz;
        public float Band1kHz { get => _band1kHz; set { _band1kHz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band2kHz;
        public float Band2kHz { get => _band2kHz; set { _band2kHz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band4kHz;
        public float Band4kHz { get => _band4kHz; set { _band4kHz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band8kHz;
        public float Band8kHz { get => _band8kHz; set { _band8kHz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        private float _band16kHz;
        public float Band16kHz { get => _band16kHz; set { _band16kHz = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); } }

        // COMPRESOR / LIMITADOR BROADCAST
        private float _compThreshold = -12f;
        public float CompThreshold { get => _compThreshold; set { _compThreshold = Math.Clamp(value, -36f, 0f); OnPropertyChanged(); } }

        private float _compRatio = 4.0f;
        public float CompRatio { get => _compRatio; set { _compRatio = Math.Clamp(value, 1.0f, 10.0f); OnPropertyChanged(); } }

        private float _compAttack = 10f;
        public float CompAttack { get => _compAttack; set { _compAttack = Math.Clamp(value, 1f, 100f); OnPropertyChanged(); } }

        private float _compRelease = 100f;
        public float CompRelease { get => _compRelease; set { _compRelease = Math.Clamp(value, 10f, 1000f); OnPropertyChanged(); } }

        private float _compMakeup = 3f;
        public float CompMakeup { get => _compMakeup; set { _compMakeup = Math.Clamp(value, 0f, 12f); OnPropertyChanged(); } }

        private string _selectedEqPreset = "Flat (Plano)";
        public string SelectedEqPreset
        {
            get => _selectedEqPreset;
            set
            {
                _selectedEqPreset = value;
                OnPropertyChanged();
                ApplyEqPreset(value);
            }
        }

        private string _selectedCompPreset = "FM Broadcast Loudness";
        public string SelectedCompPreset
        {
            get => _selectedCompPreset;
            set
            {
                _selectedCompPreset = value;
                OnPropertyChanged();
                ApplyCompressorPreset(value);
            }
        }

        public List<string> EqPresets { get; } = new List<string>
        {
            "Flat (Plano)",
            "Radio Pop FM",
            "Voz Calida Locutor",
            "Rock FM Potente",
            "Bass Maximizer"
        };

        public List<string> CompressorPresets { get; } = new List<string>
        {
            "FM Broadcast Loudness",
            "Compresión Suave",
            "Voz de Cabina",
            "Peak Limiter Hard"
        };

        public void ApplyEqPreset(string presetName)
        {
            switch (presetName)
            {
                case "Radio Pop FM":
                    SetEqValues(2f, 3f, 2f, 0f, -1f, 1f, 3f, 4f, 4f, 5f);
                    break;
                case "Voz Calida Locutor":
                    SetEqValues(-2f, -1f, 2f, 4f, 3f, 2f, 1f, 2f, 1f, 0f);
                    break;
                case "Rock FM Potente":
                    SetEqValues(4f, 5f, 3f, 0f, -2f, 1f, 3f, 4f, 5f, 6f);
                    break;
                case "Bass Maximizer":
                    SetEqValues(6f, 6f, 5f, 3f, 1f, 0f, 0f, 0f, 1f, 1f);
                    break;
                case "Flat (Plano)":
                default:
                    SetEqValues(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                    break;
            }
        }

        private void SetEqValues(float b31, float b62, float b125, float b250, float b500, float b1k, float b2k, float b4k, float b8k, float b16k)
        {
            Band31Hz = b31;
            Band62Hz = b62;
            Band125Hz = b125;
            Band250Hz = b250;
            Band500Hz = b500;
            Band1kHz = b1k;
            Band2kHz = b2k;
            Band4kHz = b4k;
            Band8kHz = b8k;
            Band16kHz = b16k;
        }

        public void ApplyCompressorPreset(string presetName)
        {
            switch (presetName)
            {
                case "Compresión Suave":
                    CompThreshold = -8f; CompRatio = 2.5f; CompAttack = 20f; CompRelease = 150f; CompMakeup = 1.5f;
                    break;
                case "Voz de Cabina":
                    CompThreshold = -18f; CompRatio = 5.0f; CompAttack = 5f; CompRelease = 80f; CompMakeup = 4.0f;
                    break;
                case "Peak Limiter Hard":
                    CompThreshold = -3f; CompRatio = 10.0f; CompAttack = 1f; CompRelease = 50f; CompMakeup = 0.0f;
                    break;
                case "FM Broadcast Loudness":
                default:
                    CompThreshold = -15f; CompRatio = 4.0f; CompAttack = 10f; CompRelease = 100f; CompMakeup = 3.5f;
                    break;
            }
        }
    }
}
