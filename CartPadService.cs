using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// Lo montado en la botonera SFX, en disco.
    ///
    /// Hasta UI-8 la botonera se reconstruia cada arranque escaneando la carpeta <c>efectos</c>
    /// y asignando los archivos por orden de aparición: mover un MP3 de sitio cambiaba de pad,
    /// y el volumen o el ducking que el operador ajustaba para el efecto de la cortinilla
    /// duraba hasta la siguiente ventana. Este servicio guarda los doce pads con su clip, su
    /// nombre, su volumen, su profundidad de ducking y su modo de disparo.
    ///
    /// Escribe como <c>schedule.json</c> y <c>track_metadata.json</c>: un <c>.tmp</c>, la copia
    /// anterior a <c>cart_pads_backup.json</c> y el movimiento encima. Un corte de luz a media
    /// escritura no puede dejar la botonera del aire sin contenido.
    /// </summary>
    public sealed class CartPadService
    {
        private static readonly ILogger Log_ = Log.ForContext<CartPadService>();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private sealed class PadsFile
        {
            public int Version { get; set; } = 1;
            public List<PadEntry> Pads { get; set; } = new List<PadEntry>();
        }

        private sealed class PadEntry
        {
            public int PadId { get; set; }
            public string Shortcut { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string ColorHex { get; set; } = string.Empty;
            public int VolumePercent { get; set; } = 100;
            public int DuckPercent { get; set; }
            public string Mode { get; set; } = nameof(CartPlayMode.Toggle);
        }

        /// <summary>Qué dijo la última lectura del disco; la consola lo pone en su estado.</summary>
        public string LastLoadMessage { get; private set; } = "Sin lectura de cart_pads.json todavía.";

        /// <summary>Dónde se lee y se escribe; depende de QUASAR_RADIO_DATA_DIR como los otros JSON.</summary>
        public string FilePath { get; } = Path.Combine(AuthService.DataDirectory(), "cart_pads.json");

        /// <summary>
        /// Aplica el disco sobre los pads ya creados. Devuelve el mensaje para el estado de la
        /// botonera. Un pad que no sale en el archivo se deja con lo que tuviera (el escaneo de
        /// <c>efectos/</c>), y un pad cuyo archivo ya no está conserva su nombre y su ruta para
        /// que el operador vea que falta, en vez de perder la asignación en silencio.
        /// </summary>
        public string Load(IEnumerable<CartPadItem> pads)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return LastLoadMessage = "Botonera vacía: sin cart_pads.json.";
                }

                var parsed = JsonSerializer.Deserialize<PadsFile>(
                    File.ReadAllText(FilePath), JsonOptions);
                if (parsed?.Pads is null)
                {
                    return LastLoadMessage = "cart_pads.json no se entendió: se queda la botonera en memoria.";
                }

                var byId = pads.ToDictionary(p => p.PadId);
                int applied = 0, missing = 0;

                foreach (var entry in parsed.Pads)
                {
                    if (!byId.TryGetValue(entry.PadId, out var pad)) continue;

                    if (!string.IsNullOrWhiteSpace(entry.Shortcut)) pad.ShortcutText = entry.Shortcut;
                    if (!string.IsNullOrWhiteSpace(entry.Title)) pad.Title = entry.Title;
                    if (!string.IsNullOrWhiteSpace(entry.FilePath)) pad.FilePath = entry.FilePath;
                    if (!string.IsNullOrWhiteSpace(entry.ColorHex)) pad.ColorHex = entry.ColorHex;

                    pad.Volume = Math.Clamp(entry.VolumePercent, 0, 100) / 100f;
                    pad.DuckDepth = Math.Clamp(entry.DuckPercent, 0, 100) / 100f;
                    if (Enum.TryParse<CartPlayMode>(entry.Mode, ignoreCase: true, out var mode))
                    {
                        pad.Mode = mode;
                    }

                    applied++;
                    if (pad.IsFileMissing) missing++;
                }

                Log_.Information("Botonera leída de {Path}: {Applied} pad(s), {Missing} sin archivo",
                    FilePath, applied, missing);

                var message = string.Format("Botonera desde cart_pads.json: {0} pad(s).", applied);
                if (missing > 0) message += string.Format(" {0} sin archivo en disco.", missing);
                return LastLoadMessage = message;
            }
            catch (Exception exception)
            {
                Log_.Error(exception, "No se pudo leer cart_pads.json");
                return LastLoadMessage = "No se pudo leer cart_pads.json: " +
                       UserErrorMessages.For(exception, "cargar la botonera");
            }
        }

        /// <summary>
        /// Escribe los doce pads tal como están en memoria. Devuelve el mensaje para el estado;
        /// nunca lanza, porque se llama desde el mismo hilo que pulsa el menú y un disco lleno no
        /// puede cortar el aire.
        /// </summary>
        public string Save(IEnumerable<CartPadItem> pads)
        {
            try
            {
                var file = new PadsFile { Version = 1 };

                foreach (var pad in pads.OrderBy(p => p.PadId))
                {
                    file.Pads.Add(new PadEntry
                    {
                        PadId = pad.PadId,
                        Shortcut = pad.ShortcutText,
                        Title = pad.Title,
                        FilePath = pad.FilePath,
                        ColorHex = pad.ColorHex,
                        VolumePercent = pad.VolumePercent,
                        DuckPercent = pad.DuckPercent,
                        Mode = pad.Mode.ToString()
                    });
                }

                string directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);

                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, Path.Combine(directory, "cart_pads_backup.json"), overwrite: true);
                }
                File.Move(temp, FilePath, overwrite: true);

                int montados = file.Pads.Count(p => !string.IsNullOrEmpty(p.FilePath));
                Log_.Information("Botonera guardada: {Total} pad(s), {Montados} con clip, en {Path}",
                    file.Pads.Count, montados, FilePath);

                return string.Format("Botonera guardada: {0} pad(s) con clip en cart_pads.json.", montados);
            }
            catch (Exception exception)
            {
                Log_.Error(exception, "No se pudo guardar cart_pads.json");
                return "No se pudo guardar la botonera: " +
                       UserErrorMessages.For(exception, "guardar la botonera");
            }
        }
    }
}
