using System;
using System.IO;

namespace quasar_radio
{
    /// <summary>
    /// Cómo se comporta una pulsación de pad.
    /// </summary>
    public enum CartPlayMode
    {
        /// <summary>
        /// Inicio/parada: la primera pulsación saca el efecto y la siguiente lo corta a mano.
        /// Es lo que hacía la botonera hasta UI-8 y lo que quiere quien dispara una cortinilla
        /// y la corta cuando entra la voz.
        /// </summary>
        Toggle,

        /// <summary>
        /// Disparar y dejar: cada pulsación vuelve a empezar el clip desde cero y se deja terminar
        /// solo. Sirve para el golpe o el efecto corto que no se va a estar cortando.
        /// </summary>
        OneShot
    }

    public class CartPadItem : ViewModelBase
    {
        private int _padId;
        public int PadId
        {
            get => _padId;
            set { _padId = value; OnPropertyChanged(); }
        }

        private string _shortcutText = string.Empty;
        public string ShortcutText
        {
            get => _shortcutText;
            set { _shortcutText = value; OnPropertyChanged(); }
        }

        private string _title = "Vacío";
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TriggerMenuHeader));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAudio));
                OnPropertyChanged(nameof(IsFileMissing));
                OnPropertyChanged(nameof(ContentStateText));
                OnPropertyChanged(nameof(FaceFooterText));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        public bool HasAudio => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

        /// <summary>
        /// El pad tiene una ruta guardada pero el archivo ya no está. Existe porque la botonera
        /// se guarda en <c>cart_pads.json</c> y la librería real vive en el directorio de emisión:
        /// mover de máquina o borrar un MP3 no puede dejar al pad pareciendo vacío y mudo.
        /// </summary>
        public bool IsFileMissing => !string.IsNullOrEmpty(FilePath) && !File.Exists(FilePath);

        /// <summary>Lo que se lee en la cara del pad cuando no hay clip montado.</summary>
        public string ContentStateText => HasAudio ? string.Empty
            : IsFileMissing ? "SIN ARCHIVO" : "VACÍO";

        private string _colorHex = "#262D3D";
        public string ColorHex
        {
            get => _colorHex;
            set { _colorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(PadTextHex)); }
        }

        /// <summary>
        /// La tinta de la pastilla F1..F12, elegida por luminancia del color de identidad del pad.
        /// UI-9: el foreground era <c>Brush.Text.OnAccent</c>, y ese token significa "negro sobre
        /// acento claro" en los temas oscuros y "blanco sobre acento oscuro" en el claro. Doce
        /// colores de pad son identidad de cabina y no cambian con la piel, asi que un amarillo
        /// #FFEA00 habria quedado con letra blanca encima en el tema claro. Con esto la pastilla
        /// se lee en cualquier tema con cualquier pad montado.
        /// </summary>
        public string PadTextHex => IsLightColor(_colorHex) ? "#000000" : "#FFFFFF";

        private static bool IsLightColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 7 || hex[0] != '#') return true;
            if (!int.TryParse(hex.AsSpan(1, 6), System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return true;
            double r = ((rgb >> 16) & 0xFF) / 255d, g = ((rgb >> 8) & 0xFF) / 255d, b = (rgb & 0xFF) / 255d;
            static double Chan(double c) => c <= 0.03928 ? c / 12.92 : System.Math.Pow((c + 0.055) / 1.055, 2.4);
            return 0.2126 * Chan(r) + 0.7152 * Chan(g) + 0.0722 * Chan(b) > 0.18;
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TriggerMenuHeader));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        private bool _isPreListening;
        public bool IsPreListening
        {
            get => _isPreListening;
            set
            {
                _isPreListening = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreListenMenuHeader));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        /// <summary>
        /// El menu contextual de este pad, construido por <see cref="CartWallViewModel"/> al montar
        /// la botonera. Va en el propio pad porque el menu vive en un Popup fuera del arbol visual
        /// de la ventana: cualquier enlace relativo a la ventana (<c>AncestorType=Window</c>) deja
        /// de resolver ahi dentro, y eso es exactamente lo que hacia que el menu de la botonera
        /// llegara vacio de funciones. Con los mandos ya atados al pad, el arbol de enlaces muere
        /// en el propio dato.
        /// </summary>
        public CartPadMenu? Menu { get; set; }

        /// <summary>La primera línea del menu: lo que hace pulsar el pad ahora mismo.</summary>
        public string TriggerMenuHeader => IsPlaying
            ? $"■ Cortar «{Title}»"
            : $"▶ Sacar al aire «{Title}»";

        public string PreListenMenuHeader => IsPreListening
            ? "Quitar la pre-escucha"
            : "🎧 Pre-escuchar por el monitor";

        // ------------------------------------------------------------------ volumen

        private float _volume = 1.0f;

        /// <summary>
        /// El volumen propio del pad, 0..1, multiplicado por el de la botonera entera. Se aplica
        /// en la cadena de este clip (despues del DSP y antes del bus de salida), asi que bajarlo
        /// no baja el resto de los pads ni el master.
        /// </summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0f, 1.0f);
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercent));
                OnPropertyChanged(nameof(VolumeMenuHeader));
                OnPropertyChanged(nameof(MixLabel));
                OnPropertyChanged(nameof(FaceFooterText));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        public int VolumePercent => (int)Math.Round(_volume * 100.0);

        /// <summary>Cabecera del submenu: el valor actual se ve sin tener que adivinarlo.</summary>
        public string VolumeMenuHeader => $"Volumen del pad · {VolumePercent} %";

        // ------------------------------------------------------------------ ducking

        private float _duckDepth;

        /// <summary>
        /// Cuanto baja la musica mientras este pad esta en el aire: 0 no la toca, 0,75 la deja a
        /// un cuarto. Es el "superpuesto, bajando la musica" que se pidio para la ronda 2, aplicado
        /// clip a clip porque un golpe de percusion no pide el mismo tratamiento que una cortinilla.
        /// </summary>
        public float DuckDepth
        {
            get => _duckDepth;
            set
            {
                _duckDepth = Math.Clamp(value, 0.0f, 1.0f);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DuckPercent));
                OnPropertyChanged(nameof(DuckMenuHeader));
                OnPropertyChanged(nameof(MixLabel));
                OnPropertyChanged(nameof(FaceFooterText));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        public int DuckPercent => (int)Math.Round(_duckDepth * 100.0);

        public string DuckMenuHeader => $"Bajar la música · {DuckPercent} %";

        /// <summary>El resumen que se lee en la cara del pad: volumen y ducking de un vistazo.</summary>
        public string MixLabel => $"{VolumePercent}% · D{DuckPercent}";

        /// <summary>
        /// La linea pequena de la cara del pad. Con clip montado dice como esta mezclado; sin clip,
        /// dice por que no va a sonar nada. Es la misma linea para que el pad no crezca ni cambie
        /// de hueco cuando se monta o se desmonta un efecto en medio del aire.
        /// </summary>
        public string FaceFooterText => HasAudio ? MixLabel : ContentStateText;

        /// <summary>Todo lo que hace falta saber del pad sin abrir el menu.</summary>
        public string PadToolTip
        {
            get
            {
                var estado = IsPlaying ? " · AL AIRE" : IsPreListening ? " · en pre-escucha" : string.Empty;
                if (!HasAudio) return $"{ShortcutText}: {(IsFileMissing ? "archivo no encontrado" : "sin clip montado")}";
                return $"{ShortcutText} · {Title}{estado}\n{Path.GetFileName(FilePath)}\n" +
                       $"volumen {VolumePercent}% · música −{DuckPercent}% · " +
                       (Mode == CartPlayMode.OneShot ? "disparar y dejar" : "inicio/parada");
            }
        }

        // ------------------------------------------------------------------ modo

        private CartPlayMode _mode = CartPlayMode.Toggle;
        public CartPlayMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsToggleMode));
                OnPropertyChanged(nameof(IsOneShotMode));
                OnPropertyChanged(nameof(ModeMenuHeader));
                OnPropertyChanged(nameof(PadToolTip));
            }
        }

        public bool IsToggleMode => Mode == CartPlayMode.Toggle;
        public bool IsOneShotMode => Mode == CartPlayMode.OneShot;
        public string ModeMenuHeader => Mode == CartPlayMode.OneShot
            ? "Modo de disparo · cada pulsación reinicia el clip"
            : "Modo de disparo · la segunda pulsación corta";

        /// <summary>Qué dice el pad cuando no tiene clip y alguien lo pulsa.</summary>
        public string FallbackTitle => $"Efecto {PadId}";
    }
}
