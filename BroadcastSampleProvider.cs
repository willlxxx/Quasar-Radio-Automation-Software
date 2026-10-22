using NAudio.Dsp;
using NAudio.Wave;
using System.Threading;

namespace quasar_radio;

public sealed class LevelMeterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public LevelMeterSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public float CurrentPeak { get; private set; }
    public float CurrentRms { get; private set; }

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);
        if (samplesRead == 0)
        {
            CurrentPeak = 0f;
            CurrentRms = 0f;
            return 0;
        }

        float sumSquares = 0f;
        float peak = 0f;

        for (var index = 0; index < samplesRead; index++)
        {
            var sample = buffer[index];
            var abs = MathF.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }

            sumSquares += sample * sample;
        }

        var rms = MathF.Sqrt(sumSquares / samplesRead);
        CurrentPeak = CurrentPeak * 0.72f + peak * 0.28f;
        CurrentRms = CurrentRms * 0.72f + rms * 0.28f;
        return samplesRead;
    }
}

public sealed class BroadcastSampleProvider : ISampleProvider
{
    private sealed class SpectrumAnalyzer
    {
        private const int FftSize = 2048;
        private const int FftExponent = 11;
        private const int BandCount = 18;
        private const float MinimumFrequency = 40f;
        private readonly float[] _samples = new float[FftSize];
        private readonly float[] _window = new float[FftSize];
        private readonly Complex[] _fftBuffer = new Complex[FftSize];
        private readonly int[] _bandStarts = new int[BandCount];
        private readonly int[] _bandEnds = new int[BandCount];
        private float[] _spectrum = new float[BandCount];
        private int _writePosition;

        public SpectrumAnalyzer(int sampleRate)
        {
            var maximumFrequency = Math.Min(16000f, sampleRate / 2f);

            for (var index = 0; index < FftSize; index++)
            {
                _window[index] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * index / (FftSize - 1));
            }

            for (var band = 0; band < BandCount; band++)
            {
                var lowFrequency = MinimumFrequency * MathF.Pow(maximumFrequency / MinimumFrequency, (float)band / BandCount);
                var highFrequency = MinimumFrequency * MathF.Pow(maximumFrequency / MinimumFrequency, (float)(band + 1) / BandCount);
                _bandStarts[band] = Math.Max(1, (int)(lowFrequency * FftSize / sampleRate));
                _bandEnds[band] = Math.Min(FftSize / 2, Math.Max(_bandStarts[band] + 1, (int)MathF.Ceiling(highFrequency * FftSize / sampleRate)));
            }
        }

        public float[] Spectrum => Volatile.Read(ref _spectrum);

        public void AddSample(float sample)
        {
            _samples[_writePosition++] = sample;
            if (_writePosition < FftSize)
            {
                return;
            }

            for (var index = 0; index < FftSize; index++)
            {
                _fftBuffer[index].X = _samples[index] * _window[index];
                _fftBuffer[index].Y = 0f;
            }

            FastFourierTransform.FFT(true, FftExponent, _fftBuffer);

            var spectrum = new float[BandCount];
            for (var band = 0; band < BandCount; band++)
            {
                var bandPower = 0f;
                for (var bin = _bandStarts[band]; bin < _bandEnds[band]; bin++)
                {
                    var real = _fftBuffer[bin].X;
                    var imaginary = _fftBuffer[bin].Y;
                    bandPower += real * real + imaginary * imaginary;
                }

                var amplitude = MathF.Sqrt(bandPower) * 4f / FftSize;
                var decibels = 20f * MathF.Log10(Math.Max(amplitude, 0.00001f));
                spectrum[band] = Math.Clamp((decibels + 60f) / 60f, 0f, 1f);
            }

            Volatile.Write(ref _spectrum, spectrum);
            _writePosition = 0;
        }
    }

    private static readonly float[] BandFrequencies = [31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f];
    private readonly ISampleProvider _source;
    private readonly AudioDspProcessor _settings;
    private readonly SpectrumAnalyzer _spectrumAnalyzer;
    private readonly BiQuadFilter[][] _filters;
    private readonly float[] _configuredGains = Enumerable.Repeat(float.NaN, BandFrequencies.Length).ToArray();
    private float _agcEnvelope;
    private float _agcGain = 1f;
    private float _compressorEnvelope;
    private float _limiterGain = 1f;
    private float _smoothedLevel;

    public BroadcastSampleProvider(ISampleProvider source, AudioDspProcessor settings)
    {
        _source = source;
        _settings = settings;
        _spectrumAnalyzer = new SpectrumAnalyzer(source.WaveFormat.SampleRate);
        _filters = Enumerable.Range(0, source.WaveFormat.Channels)
            .Select(_ => new BiQuadFilter[BandFrequencies.Length])
            .ToArray();
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public float[] Spectrum => _spectrumAnalyzer.Spectrum;
    public float CurrentLevel { get; private set; }

    public int Read(Span<float> buffer)
    {
        var samplesRead = _source.Read(buffer);
        if (samplesRead == 0)
        {
            return 0;
        }

        Span<float> gains = stackalloc float[]
        {
            _settings.Band31Hz, _settings.Band62Hz, _settings.Band125Hz, _settings.Band250Hz,
            _settings.Band500Hz, _settings.Band1kHz, _settings.Band2kHz, _settings.Band4kHz,
            _settings.Band8kHz, _settings.Band16kHz
        };
        UpdateFilters(gains);

        var channels = WaveFormat.Channels;
        var framesRead = samplesRead / channels;
        var sampleRate = WaveFormat.SampleRate;
        var agcEnabled = _settings.IsAgcEnabled;
        var agcTarget = MathF.Pow(10f, _settings.AgcTargetDb / 20f);
        var thresholdDb = _settings.CompThreshold;
        var ratio = _settings.CompRatio;
        var makeupGain = MathF.Pow(10f, _settings.CompMakeup / 20f);
        var inputGain = _settings.InputGain * _settings.MasterDspGain;
        var agcAttack = TimeCoefficient(15f, sampleRate);
        var agcRelease = TimeCoefficient(350f, sampleRate);
        var compressorAttack = TimeCoefficient(_settings.CompAttack, sampleRate);
        var compressorRelease = TimeCoefficient(_settings.CompRelease, sampleRate);
        var limiterAttack = TimeCoefficient(1f, sampleRate);
        var limiterRelease = TimeCoefficient(80f, sampleRate);

        for (var frame = 0; frame < framesRead; frame++)
        {
            var frameOffset = frame * channels;
            var peak = 0f;
            var frameLevel = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = buffer[frameOffset + channel] * inputGain;
                for (var band = 0; band < BandFrequencies.Length; band++)
                {
                    sample = _filters[channel][band].Transform(sample);
                }

                buffer[frameOffset + channel] = sample;
                var absoluteSample = Math.Abs(sample);
                peak = Math.Max(peak, absoluteSample);
                frameLevel = Math.Max(frameLevel, absoluteSample);
            }

            if (agcEnabled)
            {
                _agcEnvelope = SmoothEnvelope(_agcEnvelope, peak, agcAttack, agcRelease);
                var desiredAgcGain = Math.Clamp(agcTarget / Math.Max(_agcEnvelope, 0.0001f), 0.25f, 4f);
                _agcGain += (desiredAgcGain - _agcGain) * (desiredAgcGain < _agcGain ? agcAttack : agcRelease);
            }
            else
            {
                _agcGain = 1f;
            }

            var agcPeak = peak * _agcGain;
            _compressorEnvelope = SmoothEnvelope(_compressorEnvelope, agcPeak, compressorAttack, compressorRelease);
            var levelDb = 20f * MathF.Log10(Math.Max(_compressorEnvelope, 0.0001f));
            var reductionDb = levelDb > thresholdDb
                ? (levelDb - thresholdDb) * (1f - 1f / ratio)
                : 0f;
            var compressedGain = _agcGain * makeupGain * MathF.Pow(10f, -reductionDb / 20f);
            var processedPeak = peak * compressedGain;
            var desiredLimiterGain = processedPeak > 0.95f ? 0.95f / processedPeak : 1f;
            _limiterGain += (desiredLimiterGain - _limiterGain) * (desiredLimiterGain < _limiterGain ? limiterAttack : limiterRelease);
            var outputGain = compressedGain * _limiterGain;

            var monoSample = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var index = frameOffset + channel;
                buffer[index] = Math.Clamp(buffer[index] * outputGain, -0.95f, 0.95f);
                monoSample += buffer[index];
                frameLevel = Math.Max(frameLevel, Math.Abs(buffer[index]));
            }

            var rawLevel = Math.Clamp(frameLevel, 0f, 1f);
            var logLevel = rawLevel <= 0.0001f ? 0f : (float)(((20d * Math.Log10(rawLevel + 0.0001d) + 60d) / 60d));
            logLevel = Math.Clamp(logLevel, 0f, 1f);
            _smoothedLevel = _smoothedLevel * 0.75f + logLevel * 0.25f;
            CurrentLevel = Math.Clamp(_smoothedLevel, 0f, 1f);
            _spectrumAnalyzer.AddSample(monoSample / channels);
        }

        return samplesRead;
    }

    private void UpdateFilters(ReadOnlySpan<float> gains)
    {
        var needsUpdate = false;
        for (var band = 0; band < gains.Length; band++)
        {
            if (_configuredGains[band] != gains[band])
            {
                needsUpdate = true;
                break;
            }
        }

        if (!needsUpdate)
        {
            return;
        }

        var sampleRate = WaveFormat.SampleRate;
        for (var band = 0; band < gains.Length; band++)
        {
            var frequency = Math.Min(BandFrequencies[band], sampleRate * 0.45f);
            for (var channel = 0; channel < _filters.Length; channel++)
            {
                _filters[channel][band] = BiQuadFilter.PeakingEQ(sampleRate, frequency, 1f, gains[band]);
            }

            _configuredGains[band] = gains[band];
        }
    }

    private static float SmoothEnvelope(float envelope, float input, float attack, float release) =>
        envelope + (input - envelope) * (input > envelope ? attack : release);

    private static float TimeCoefficient(float milliseconds, int sampleRate) =>
        1f - MathF.Exp(-1f / (milliseconds * 0.001f * sampleRate));
}