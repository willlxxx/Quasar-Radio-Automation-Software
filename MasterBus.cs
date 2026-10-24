using System;
using System.Collections.Generic;
using System.Linq;

namespace quasar_radio;

/// <summary>
/// Lo que de verdad sale por la tarjeta.
///
/// Cada fuente que tiene su propia salida (deck A, deck B, botonera SFX y micro) empuja aqui su
/// nivel <b>post-fader</b> —con el volumen del canal, el master y el ducking ya aplicados— desde
/// el hilo de audio. El unico que lee es el temporizador de medicion de la consola, una vez cada
/// 100 ms.
///
/// Hace falta porque la consola no tiene un bus comun: son cuatro <c>WaveOut</c> independientes
/// y el motor de reproducecion no ve lo que suena en la botonera ni lo que dice el micro. Hasta
/// UI-7 el "VU MASTER" de la pantalla se alimentaba del nivel <i>previo</i> al fader del deck
/// activo, asi que con el master bajado seguia pintando tope y con un efecto de la botonera en
/// el aire se quedaba clavado a cero.
///
/// Una fuente que deja de emitir deja de empujar: si su ultima muestra es mas vieja que la
/// ventana de <see cref="Sample"/>, se la ignora. Por eso no hace falta un "stop" explicito.
/// </summary>
public sealed class MasterBus
{
    private readonly object _sync = new object();
    private readonly Dictionary<string, Source> _sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);

    private sealed class Source
    {
        public float Left;
        public float Right;
        public DateTime LastSampleUtc;
    }

    /// <summary>
    /// Lo que llama el <see cref="MasterLevelTap"/> de cada cadena de audio, en el hilo de WaveOut.
    /// Los niveles van en lineal 0..1, ya despues del volumen.
    /// </summary>
    public void PushLevel(string sourceId, float left, float right)
    {
        if (string.IsNullOrEmpty(sourceId)) return;

        lock (_sync)
        {
            if (!_sources.TryGetValue(sourceId, out var source))
            {
                source = new Source();
                _sources[sourceId] = source;
            }

            source.Left = Math.Clamp(left, 0f, 1f);
            source.Right = Math.Clamp(right, 0f, 1f);
            source.LastSampleUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// El maximo por canal entre las fuentes vistas en los ultimos <paramref name="windowMs"/>
    /// milisegundos. Devuelve false si ninguna fuente ha empujado recently: la consola pinta
    /// entonces los medidores a cero y el detector de silencio sabe que hay que vigilar.
    /// </summary>
    public bool Sample(out float left, out float right, int windowMs = 250)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-windowMs);
        var bestLeft = 0f;
        var bestRight = 0f;
        var any = false;

        lock (_sync)
        {
            foreach (var source in _sources.Values)
            {
                if (source.LastSampleUtc < cutoff) continue;
                any = true;
                if (source.Left > bestLeft) bestLeft = source.Left;
                if (source.Right > bestRight) bestRight = source.Right;
            }
        }

        left = bestLeft;
        right = bestRight;
        return any;
    }

    /// <summary>Fuentes vistas en la ultima ventana. Solo para diagnostico y verificacion.</summary>
    public int LiveSourceCount(int windowMs = 250)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-windowMs);
        lock (_sync)
        {
            var count = 0;
            foreach (var source in _sources.Values)
            {
                if (source.LastSampleUtc >= cutoff) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// El nivel post-fader de UNA fuente concreta (UI-8). Hace falta para medir que el volumen de
    /// un pad o el ducking de la musica son de verdad suyos: con solo la mezcla total, un pad que
    /// suena mientras un deck esta en el aire no se puede separar de la suma de los dos.
    /// Devuelve false si la fuente no ha empujado en la ventana.
    /// </summary>
    public bool Sample(string sourceId, out float left, out float right, int windowMs = 250)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-windowMs);
        left = 0f;
        right = 0f;

        if (string.IsNullOrEmpty(sourceId)) return false;

        lock (_sync)
        {
            if (!_sources.TryGetValue(sourceId, out var source)) return false;
            if (source.LastSampleUtc < cutoff) return false;

            left = source.Left;
            right = source.Right;
            return true;
        }
    }

    /// <summary>Los ids de las fuentes vivas, ordenados. Para el estado de la consola y las mediciones.</summary>
    public string LiveSources(int windowMs = 250)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-windowMs);
        lock (_sync)
        {
            var vivas = _sources.Where(kv => kv.Value.LastSampleUtc >= cutoff)
                                .Select(kv => kv.Key)
                                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", vivas);
        }
    }

    /// <summary>Se llama al parar una fuente para que su ultimo nivel no cuente como aire.</summary>
    public void Forget(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId)) return;
        lock (_sync)
        {
            _sources.Remove(sourceId);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _sources.Clear();
        }
    }
}
