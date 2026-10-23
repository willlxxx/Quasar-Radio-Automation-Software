using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// Lo que una celda de la rejilla horaria manda hacer cuando llega su hora.
    /// Cada valor tiene que corresponder a algo que la emisora ya es capaz de hacer
    /// por si sola; si no, no entra en la rejilla (UI-4 paso C).
    /// </summary>
    public enum ScheduleKind
    {
        /// <summary>Celda libre: no programa nada y no interfiere con el operador.</summary>
        Nada = 0,

        /// <summary>Hora hablada: los clips <c>HRSxx</c> / <c>HRSxx_O</c> de <c>horarias/</c>,
        /// y <see cref="TimeAnnouncementService"/> si ese clip no existe.</summary>
        Hora = 1,

        /// <summary>Un pad de la botonera SFX (F1–F12).</summary>
        Cart = 2,

        /// <summary>Una tanda comercial existente, inyectada en la cola de emisión.</summary>
        Tanda = 3,

        /// <summary>Regenera la lista automática desde la librería y la pone en el aire.</summary>
        AutoPlaylist = 4
    }

    /// <summary>
    /// Una pauta programada en un día y una hora concretos. Es el registro que se
    /// guarda en <c>schedule.json</c>; la posición (día, hora) la da la celda que lo
    /// contiene, así que aquí solo viaja lo que el operador elige.
    /// </summary>
    public sealed class ScheduleSlot
    {
        public int Minute { get; set; }

        public ScheduleKind Kind { get; set; } = ScheduleKind.Nada;

        /// <summary>
        /// Referencia dentro del tipo: el atajo del pad (<c>F3</c>), el identificador de
        /// la tanda (<c>a1b2c3d4</c>) o vacío para <see cref="ScheduleKind.Hora"/> y
        /// <see cref="ScheduleKind.AutoPlaylist"/>.
        /// </summary>
        public string Ref { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public ScheduleSlot Clone() => new()
        {
            Minute = Minute,
            Kind = Kind,
            Ref = Ref,
            Enabled = Enabled
        };

        public bool IsEmpty => Kind == ScheduleKind.Nada;
    }

    /// <summary>
    /// La rejilla horaria de la emisora: siete días × veinticuatro horas, editable desde
    /// el módulo Programación y cumplida por un reloj de un segundo.
    ///
    /// UI-4 paso C (2026-10-23). Antes de esto la única pauta que existía eran dos tandas
    /// fijadas a mano en <c>InitializeCommercialBlocks()</c> que se disparaban «cada hora a
    /// los :MM» sin preguntar en qué hora ni en qué día, y se perdían al cerrar el programa.
    ///
    /// El servicio no sabe de ventanas ni de comandos: guarda las celdas, persiste, y cuando
    /// llega el minuto de una celda lanza <see cref="SlotDue"/>. Quien ejecuta la pauta es
    /// <see cref="MainViewModel"/>, que es el único que tiene a mano el motor de audio, la
    /// botonera, las tandas y la biblioteca.
    /// </summary>
    public sealed class WeeklyScheduleService
    {
        public const int DaysPerWeek = 7;
        public const int HoursPerDay = 24;
        public const int CellCount = DaysPerWeek * HoursPerDay;

        private static readonly ILogger Log_ = Log.ForContext<WeeklyScheduleService>();

        private readonly ScheduleSlot?[] _cells = new ScheduleSlot?[CellCount];
        private readonly Dictionary<int, string> _lastFired = new();
        private readonly DispatcherTimer _timer;

        /// <summary>Nombres de los días en el orden de la rejilla: la semana radiofónica
        /// empieza en lunes, aunque <see cref="Day"/> se guarde como
        /// <see cref="DayOfWeek"/> (0 = domingo).</summary>
        public static readonly int[] GridDayOrder = [1, 2, 3, 4, 5, 6, 0];

        public static readonly string[] DayNames =
            ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

        /// <summary>Índice de <see cref="DayNames"/> para un <see cref="DayOfWeek"/>.</summary>
        public static string DayName(int day) =>
            day >= 0 && day < DayNames.Length ? DayNames[day] : "?";

        /// <summary>
        /// Nombre corto para la cabecera de la rejilla y las fichas de día. En español
        /// miércoles se abrevia X porque la M ya es de martes; sin este detalle las dos
        /// columnas centrales de la semana dirían lo mismo.
        /// </summary>
        public static string DayInitial(int day) =>
            day == 3 ? "X" : DayName(day).Substring(0, 1).ToUpperInvariant();

        public WeeklyScheduleService()
        {
            for (int i = 0; i < CellCount; i++) _cells[i] = null;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => RunDueAt(DateTime.Now);
            _timer.Start();
        }

        /// <summary>Se dispara con la celda que toca, una sola vez por ocurrencia.</summary>
        public event Action<int, int, ScheduleSlot>? SlotDue;

        /// <summary>Cambia el estado de una celda y avisa de que hay trabajo sin guardar.</summary>
        public event Action? Changed;

        public string FilePath { get; } = Path.Combine(AuthService.DataDirectory(), "schedule.json");

        public bool Dirty { get; private set; }

        public int ProgrammedCount => _cells.Count(c => c is { IsEmpty: false });

        private static int IndexOf(int day, int hour) =>
            day >= 0 && day < DaysPerWeek && hour >= 0 && hour < HoursPerDay
                ? day * HoursPerDay + hour
                : -1;

        /// <summary>La pauta de una celda, o <c>null</c> si la celda está libre.</summary>
        public ScheduleSlot? At(int day, int hour)
        {
            int i = IndexOf(day, hour);
            return i < 0 ? null : _cells[i];
        }

        /// <summary>
        /// Deja una pauta en la celda. Pasar <c>null</c> o una celda <see cref="ScheduleKind.Nada"/>
        /// la deja libre. Se guarda una copia: quien llama puede seguir moviendo su objeto.
        /// </summary>
        public void Set(int day, int hour, ScheduleSlot? slot)
        {
            int i = IndexOf(day, hour);
            if (i < 0) return;

            _cells[i] = slot == null || slot.IsEmpty ? null : slot.Clone();
            Dirty = true;
            Changed?.Invoke();
        }

        /// <summary>Toda la rejilla de una vez, para el ViewModel.</summary>
        public ScheduleSlot?[] Snapshot() => (ScheduleSlot?[])_cells.Clone();

        // ---------------------------------------------------------------- proxima pauta

        /// <summary>
        /// Próxima celda que se va a disparar después de <paramref name="from"/>, mirando
        /// hacia delante hasta siete días. Devuelve <c>null</c> si la rejilla está vacía.
        /// </summary>
        public (int Day, int Hour, int Minute, ScheduleSlot Slot)? NextDue(DateTime from)
        {
            for (int offset = 0; offset < DaysPerWeek; offset++)
            {
                DateTime probe = from.Date.AddDays(offset);
                int day = (int)probe.DayOfWeek;
                int startHour = offset == 0 ? from.Hour : 0;
                int startMinute = offset == 0 ? from.Minute + 1 : 0;

                for (int hour = startHour; hour < HoursPerDay; hour++)
                {
                    var slot = _cells[IndexOf(day, hour)];
                    if (slot is not { IsEmpty: false, Enabled: true }) continue;
                    if (hour == startHour && slot.Minute < startMinute) continue;
                    if (offset == 0 && hour == from.Hour && slot.Minute <= from.Minute) continue;
                    return (day, hour, slot.Minute, slot);
                }
            }

            return null;
        }

        /// <summary>«próxima: mié 14:00 · Tanda Minuto 15», para la cabecera del módulo.</summary>
        public string NextDueText(DateTime from, Func<ScheduleSlot, string>? describe = null)
        {
            var next = NextDue(from);
            if (next is null) return "sin más pautas programadas";

            var slot = next.Value.Slot;
            string what = describe?.Invoke(slot) ?? Describe(slot);
            return string.Format("próxima: {0} {1:00}:{2:00} · {3}",
                DayName(next.Value.Day), next.Value.Hour, slot.Minute, what);
        }

        /// <summary>Texto legible de una pauta, con la referencia ya resuelta si se puede.</summary>
        public static string Describe(ScheduleSlot? slot) => slot switch
        {
            null => "libre",
            { Kind: ScheduleKind.Nada } => "libre",
            { Kind: ScheduleKind.Hora } => "hora hablada",
            { Kind: ScheduleKind.Cart } => "cart " + (string.IsNullOrWhiteSpace(slot.Ref) ? "sin pad" : slot.Ref),
            { Kind: ScheduleKind.Tanda } => "tanda " + (string.IsNullOrWhiteSpace(slot.Ref) ? "sin elegir" : slot.Ref),
            { Kind: ScheduleKind.AutoPlaylist } => "lista automática",
            _ => "libre"
        };

        // ---------------------------------------------------------------- el reloj

        /// <summary>
        /// Aplica las celdas que tocan en <paramref name="now"/>. Es una función del instante
        /// que se le pase, no de <see cref="DateTime.Now"/>, para que el criterio se pueda
        /// razonar (y probar) sin esperar a que llegue la hora.
        ///
        /// El disparo va por <b>minuto de la celda</b> y por <b>ocurrencia</b>, no por
        /// <c>Second == 0</c>: el <see cref="DispatcherTimer"/> de un segundo tiene jitter, y
        /// comparar el segundo exacto hacía que una tanda no se disparase nunca en la corrida
        /// en la que el tick caía en 0,999 o en 1,004.
        /// </summary>
        public int RunDueAt(DateTime now)
        {
            int day = (int)now.DayOfWeek;
            int hour = now.Hour;
            var slot = _cells[IndexOf(day, hour)];
            if (slot is not { IsEmpty: false, Enabled: true }) return 0;
            if (slot.Minute != now.Minute) return 0;

            int index = IndexOf(day, hour);
            string stamp = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            if (_lastFired.TryGetValue(index, out string? fired) && fired == stamp) return 0;

            _lastFired[index] = stamp;
            Log_.Information("Pauta horaria: {Day} {Hour:00}:{Minute:00} -> {What}",
                DayName(day), hour, slot.Minute, Describe(slot));
            SlotDue?.Invoke(day, hour, slot);
            return 1;
        }

        // ---------------------------------------------------------------- persistencia

        private sealed class ScheduleFile
        {
            public int Version { get; set; } = 1;
            public List<ScheduleEntry> Slots { get; set; } = new();
        }

        private sealed class ScheduleEntry
        {
            public int Day { get; set; }
            public int Hour { get; set; }
            public int Minute { get; set; }
            public string Kind { get; set; } = nameof(ScheduleKind.Nada);
            public string Ref { get; set; } = string.Empty;
            public bool Enabled { get; set; } = true;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        /// <summary>Qué dijo la última lectura del disco; el módulo lo pone en su estado.</summary>
        public string LastLoadMessage { get; private set; } = "Sin lectura de schedule.json todavía.";

        /// <summary>Carga <c>schedule.json</c> si existe. Devuelve un mensaje para el estado.</summary>
        public string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return LastLoadMessage = "Programación vacía: sin schedule.json.";

                var parsed = JsonSerializer.Deserialize<ScheduleFile>(
                    File.ReadAllText(FilePath), JsonOptions);
                if (parsed is null) return LastLoadMessage = "schedule.json no se entendió: se empieza vacío.";

                int applied = 0;
                foreach (var entry in parsed.Slots)
                {
                    if (!Enum.TryParse<ScheduleKind>(entry.Kind, ignoreCase: true, out var kind)) continue;
                    if (kind == ScheduleKind.Nada) continue;
                    if (IndexOf(entry.Day, entry.Hour) < 0) continue;

                    _cells[IndexOf(entry.Day, entry.Hour)] = new ScheduleSlot
                    {
                        Minute = Math.Clamp(entry.Minute, 0, 59),
                        Kind = kind,
                        Ref = entry.Ref ?? string.Empty,
                        Enabled = entry.Enabled
                    };
                    applied++;
                }

                Dirty = false;
                Changed?.Invoke();
                return LastLoadMessage = string.Format(
                    "Recuperada schedule.json: {0} celda(s) programadas.", applied);
            }
            catch (Exception exception)
            {
                Log_.Error(exception, "No se pudo leer schedule.json");
                return LastLoadMessage = "No se pudo leer schedule.json: " +
                       UserErrorMessages.For(exception, "cargar la programación");
            }
        }

        /// <summary>
        /// Escribe la rejilla. Atómica como en <c>track_metadata.json</c>: se escribe un
        /// <c>.tmp</c>, se guarda la copia anterior como respaldo y se mueve encima. Un fallo
        /// a media escritura no puede dejar al operador sin programación.
        /// </summary>
        public string Save()
        {
            try
            {
                var file = new ScheduleFile
                {
                    Version = 1,
                    Slots = new List<ScheduleEntry>()
                };

                for (int day = 0; day < DaysPerWeek; day++)
                {
                    for (int hour = 0; hour < HoursPerDay; hour++)
                    {
                        var slot = _cells[IndexOf(day, hour)];
                        if (slot is null) continue;
                        file.Slots.Add(new ScheduleEntry
                        {
                            Day = day,
                            Hour = hour,
                            Minute = slot.Minute,
                            Kind = slot.Kind.ToString(),
                            Ref = slot.Ref,
                            Enabled = slot.Enabled
                        });
                    }
                }

                string directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);

                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, JsonOptions));
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, Path.Combine(directory, "schedule_backup.json"), overwrite: true);
                }
                File.Move(temp, FilePath, overwrite: true);

                Dirty = false;
                Changed?.Invoke();
                Log_.Information("Programación guardada: {Count} celda(s) en {Path}",
                    file.Slots.Count, FilePath);
                return string.Format("Programación guardada: {0} celda(s) en schedule.json.", file.Slots.Count);
            }
            catch (Exception exception)
            {
                Log_.Error(exception, "No se pudo guardar schedule.json");
                return "No se pudo guardar la programación: " +
                       UserErrorMessages.For(exception, "guardar la programación");
            }
        }
    }
}
