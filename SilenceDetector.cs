using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace quasar_radio
{
    public class SilenceDetector
    {
        private readonly Queue<float> _levelHistory = new Queue<float>();
        private readonly int _historySize = 12; // ~1,2 s a un nivel por cada tick de 100 ms
        private int _silenceFrameCount = 0;
        private DateTime _silenceStartTime = DateTime.MinValue;
        
        private float _silenceThreshold = 0.05f; // -26dB aproximadamente
        private TimeSpan _minSilenceDuration = TimeSpan.FromSeconds(2.0); // 2 segundos de silencio
        private bool _isSilenceDetectionEnabled = true;
        
        public event EventHandler? SilenceDetected;

        /// <summary>
        /// Se dispara cuando el nivel vuelve por encima del umbral despues de un aviso. Antes de
        /// UI-7 no existia: el aviso se apagaba con un temporizador de 2 segundos aunque el aire
        /// siguiera mudo, o seguia encendido aunque ya estuviera sonando. Ahora el estado lo manda
        /// la senal, no el reloj.
        /// </summary>
        public event EventHandler? SilenceCleared;
        
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

        /// <summary>Hay un aviso de silencio en pie. Dura hasta que la senal vuelve.</summary>
        public bool AlertActive { get; private set; }
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
                    
                    // Un solo aviso por episodio: se queda en pie hasta que vuelva el nivel.
                    if (!AlertActive && CurrentSilenceDuration >= _minSilenceDuration)
                    {
                        AlertActive = true;
                        SilenceDetected?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            else
            {
                if (AlertActive)
                {
                    AlertActive = false;
                    SilenceCleared?.Invoke(this, EventArgs.Empty);
                }
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
            AlertActive = false;
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