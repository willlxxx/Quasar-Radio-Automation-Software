using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace quasar_radio
{
    public class SilenceDetector
    {
        private readonly Queue<float> _levelHistory = new Queue<float>();
        private readonly int _historySize = 50; // Muestras para mantener historial
        private int _silenceFrameCount = 0;
        private DateTime _silenceStartTime = DateTime.MinValue;
        
        private float _silenceThreshold = 0.05f; // -26dB aproximadamente
        private TimeSpan _minSilenceDuration = TimeSpan.FromSeconds(2.0); // 2 segundos de silencio
        private bool _isSilenceDetectionEnabled = true;
        
        public event EventHandler? SilenceDetected;
        
        public float SilenceThreshold
        {
            get => _silenceThreshold;
            set => _silenceThreshold = Math.Clamp(value, 0.001f, 0.5f);
        }
        
        public TimeSpan MinSilenceDuration
        {
            get => _minSilenceDuration;
            set => _minSilenceDuration = TimeSpan.FromMilliseconds(Math.Max(500, value.TotalMilliseconds));
        }
        
        public bool IsSilenceDetectionEnabled
        {
            get => _isSilenceDetectionEnabled;
            set => _isSilenceDetectionEnabled = value;
        }
        
        public float CurrentLevel { get; private set; }
        public bool IsInSilence { get; private set; }
        public TimeSpan CurrentSilenceDuration { get; private set; }
        
        public void ProcessLevel(float leftLevel, float rightLevel)
        {
            if (!_isSilenceDetectionEnabled)
            {
                Reset();
                return;
            }
            
            // Calcular nivel promedio de ambos canales
            float averageLevel = (leftLevel + rightLevel) / 2f;
            CurrentLevel = averageLevel;
            
            // Agregar al historial
            _levelHistory.Enqueue(averageLevel);
            if (_levelHistory.Count > _historySize)
                _levelHistory.Dequeue();
            
            // Calcular nivel promedio del historial (para evitar falsos positivos)
            float historicalAverage = CalculateHistoricalAverage();
            
            // Detectar si estamos en silencio
            bool currentlySilent = historicalAverage < _silenceThreshold;
            
            if (currentlySilent)
            {
                if (!IsInSilence)
                {
                    // Inicio de silencio
                    IsInSilence = true;
                    _silenceStartTime = DateTime.Now;
                    _silenceFrameCount = 0;
                }
                else
                {
                    // Continuación de silencio
                    _silenceFrameCount++;
                    CurrentSilenceDuration = DateTime.Now - _silenceStartTime;
                    
                    // Verificar si alcanzamos la duración mínima de silencio
                    if (CurrentSilenceDuration >= _minSilenceDuration)
                    {
                        SilenceDetected?.Invoke(this, EventArgs.Empty);
                        Reset(); // Resetear para evitar disparos múltiples
                    }
                }
            }
            else
            {
                // No hay silencio, resetear
                Reset();
            }
        }
        
        private float CalculateHistoricalAverage()
        {
            if (_levelHistory.Count == 0) return 0f;
            
            float sum = 0f;
            foreach (var level in _levelHistory)
            {
                sum += level;
            }
            return sum / _levelHistory.Count;
        }
        
        public void Reset()
        {
            IsInSilence = false;
            _silenceFrameCount = 0;
            _silenceStartTime = DateTime.MinValue;
            CurrentSilenceDuration = TimeSpan.Zero;
            _levelHistory.Clear();
        }
        
        public float GetThresholdInDecibels()
        {
            // Convertir el valor 0-1 a dB (aproximado)
            if (_silenceThreshold <= 0.001f) return -60f;
            return 20f * (float)Math.Log10(_silenceThreshold);
        }
        
        public void SetThresholdFromDecibels(float db)
        {
            // Convertir dB a valor 0-1 (aproximado)
            _silenceThreshold = (float)Math.Pow(10, db / 20f);
            _silenceThreshold = Math.Clamp(_silenceThreshold, 0.001f, 0.5f);
        }
    }
}