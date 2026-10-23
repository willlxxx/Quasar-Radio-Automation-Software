using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Serilog;

namespace quasar_radio
{
    /// <summary>Ficha de dia de la cabecera de Programación (la «S» de sábado, etc.).</summary>
    public sealed class ScheduleDayChip
    {
        public ScheduleDayChip(int day, string label, string name)
        {
            Day = day;
            Label = label;
            Name = name;
        }

        public int Day { get; }
        public string Label { get; }
        public string Name { get; }

        /// <summary>
        /// El contenedor que WPF genera para una ficha se nombra con el <c>ToString()</c> del
        /// elemento, y el de un objeto sin sobrescribir es el nombre de la clase: medido en vivo
        /// 2026-10-23 el arbol de accesibilidad tenia siete elementos llamados
        /// «quasar_radio.ScheduleDayChip». Un lector de pantalla lee eso. Con esto lee el dia.
        /// </summary>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Una de las cinco fichas de pauta del editor. Son botones y no un desplegable porque
    /// elegir qué suena es la decisión central de la pantalla: un clic, y a la vista.
    /// Además el desplegable de WPF vive en su propia ventana HWND y volverse a manejar por
    /// UIA es poco fiable; la ficha se ve y se pulsa igual que el resto de la consola.
    /// </summary>
    public sealed class ScheduleKindChip : ViewModelBase
    {
        private bool _isCurrent;

        public ScheduleKindChip(int value, string label, string name)
        {
            Value = value;
            Label = label;
            Name = name;
        }

        public int Value { get; }
        public string Label { get; }
        public string Name { get; }

        /// <summary>La ficha que corresponde a la pauta escrita en el editor.</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set { _isCurrent = value; OnPropertyChanged(); }
        }

        /// <summary>Mismo motivo que en <see cref="ScheduleDayChip"/>: el contenedor se nombra por aqui.</summary>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Celda pintada en la rejilla. Es una proyección viva de
    /// <see cref="WeeklyScheduleService"/>: al cambiar la pauta, la celda se redesenha sola.
    /// </summary>
    public sealed class ScheduleCellViewModel : ViewModelBase
    {
        private bool _isSelected;
        private bool _isNow;

        public ScheduleCellViewModel(int day, int hour)
        {
            Day = day;
            Hour = hour;
        }

        public int Day { get; }
        public int Hour { get; }

        /// <summary>Índice de la celda en el vector del servicio (día * 24 + hora).</summary>
        public int Index => Day * WeeklyScheduleService.HoursPerDay + Hour;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>La celda en la que está el reloj ahora mismo.</summary>
        public bool IsNow
        {
            get => _isNow;
            set { _isNow = value; OnPropertyChanged(); }
        }

        public ScheduleSlot? Slot { get; set; }

        public bool HasSlot => Slot is { IsEmpty: false };
        public bool IsEnabled => Slot?.Enabled ?? false;
        public ScheduleKind Kind => Slot?.Kind ?? ScheduleKind.Nada;

        public string TimeLabel => string.Format("{0:00}:00", Hour);
        public string DayName => WeeklyScheduleService.DayName(Day);
        public string DayInitial => WeeklyScheduleService.DayInitial(Day);

        /// <summary>Lo que cabe dentro del cuadrado de la rejilla.</summary>
        public string Glyph => Kind switch
        {
            ScheduleKind.Hora => "🕐",
            ScheduleKind.Cart => string.IsNullOrWhiteSpace(Slot?.Ref) ? "🎬" : Slot!.Ref.Replace("F", "F·"),
            ScheduleKind.Tanda => "📢",
            ScheduleKind.AutoPlaylist => "AUTO",
            _ => string.Empty
        };

        /// <summary>Detalle para la vista de día y para el tooltip.</summary>
        public string Detail => HasSlot
            ? string.Format("{0:00}:{1:00} · {2}", Hour, Slot!.Minute, WeeklyScheduleService.Describe(Slot))
            : string.Format("{0:00}:00 · libre", Hour);

        /// <summary>
        /// Nombre de UIA. Va por aqui y no por posicion: con la rejilla dentro de un
        /// ScrollViewer, la unica forma fiable de que una prueba (o un lector de pantalla)
        /// diga «la celda del martes a las 14» es que la celda lo diga ella misma.
        /// </summary>
        public string AutomationName => string.Format("celda {0} {1:00} · {2}",
            DayName, Hour, HasSlot ? WeeklyScheduleService.Describe(Slot) + (IsEnabled ? "" : " (pausada)") : "libre");

        public void Refresh(ScheduleSlot? slot)
        {
            Slot = slot;
            OnPropertyChanged(nameof(HasSlot));
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(AutomationName));
        }
    }

    /// <summary>
    /// ViewModel del módulo PROGRAMACIÓN (UI-4 paso C): la rejilla horaria editable en
    /// vista día y en vista semana, más las tandas que la rejilla puede programar.
    ///
    /// No toca el aire: guarda celdas en <see cref="WeeklyScheduleService"/> y pide a
    /// <see cref="MainViewModel"/> lo que ya sabe hacer (poner una tanda, disparar un cart,
    /// decir la hora). La ejecución de las pautas está ahí, no aquí, para que la rejilla se
    /// cumpla con el módulo cerrado.
    /// </summary>
    public sealed class ScheduleViewModel : ViewModelBase
    {
        private static readonly ILogger Log_ = Log.ForContext<ScheduleViewModel>();

        private readonly WeeklyScheduleService _schedule;
        private readonly DispatcherTimer _clock;
        private readonly List<ScheduleCellViewModel> _all = new();
        private ScheduleCellViewModel? _selected;
        private ScheduleCellViewModel? _nowCell;
        private ScheduleCellViewModel? _clipboard;
        private int _selectedDay = (int)DateTime.Now.DayOfWeek;
        private bool _isWeekView = true;
        private ScheduleKind _editKind = ScheduleKind.Hora;
        private int _editMinute;
        private string _editRef = string.Empty;
        private bool _editEnabled = true;
        private int _rangeEndHour = 18;
        private string _statusText;

        public ScheduleViewModel(WeeklyScheduleService schedule, MainViewModel console)
        {
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            Console = console ?? throw new ArgumentNullException(nameof(console));

            for (int hour = 0; hour < WeeklyScheduleService.HoursPerDay; hour++)
            {
                foreach (int day in WeeklyScheduleService.GridDayOrder)
                {
                    _all.Add(new ScheduleCellViewModel(day, hour));
                }
            }

            WeekCells = new ObservableCollection<ScheduleCellViewModel>(_all);
            DayCells = new ObservableCollection<ScheduleCellViewModel>();

            KindChips = new ObservableCollection<ScheduleKindChip>
            {
                new((int)ScheduleKind.Nada, "· libre", "tipo de pauta: libre"),
                new((int)ScheduleKind.Hora, "🕐 hora", "tipo de pauta: hora hablada"),
                new((int)ScheduleKind.Cart, "🎬 cart", "tipo de pauta: cart de la botonera"),
                new((int)ScheduleKind.Tanda, "📢 tanda", "tipo de pauta: tanda comercial"),
                new((int)ScheduleKind.AutoPlaylist, "♻ AUTO", "tipo de pauta: lista automatica")
            };

            // La rejilla ya la leyó la consola al arrancar (MainViewModel), para que las pautas
            // se cumplan aunque nadie abra este módulo. Aquí solo se toma lo que dijo esa lectura.
            _statusText = _schedule.LastLoadMessage;
            RefreshKindChips();
            RebuildDayCells();
            RefreshAll();
            MarkNow();

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _clock.Tick += (_, _) => MarkNow();
            _clock.Start();

            _schedule.Changed += () => { RefreshAll(); RaiseHeader(); };

            SelectCellCommand = new RelayCommand(o => SelectCell(o as ScheduleCellViewModel));
            ApplyCellCommand = new RelayCommand(_ => ApplyCell(), _ => CanOperate() && Selected != null);
            ClearCellCommand = new RelayCommand(_ => ClearCell(), _ => CanOperate() && Selected != null && Selected.HasSlot);
            FillRangeCommand = new RelayCommand(_ => FillRange(), _ => CanOperate() && Selected != null);
            ApplyToWeekCommand = new RelayCommand(_ => ApplyToWeek(), _ => CanOperate() && Selected != null);
            CopyCellCommand = new RelayCommand(_ => CopyCell(), _ => Selected != null && Selected.HasSlot);
            PasteCellCommand = new RelayCommand(_ => PasteCell(), _ => CanOperate() && Selected != null && ClipboardCell != null);
            SaveCommand = new RelayCommand(_ => Save());
            ReloadCommand = new RelayCommand(_ => Reload());
            ToggleViewCommand = new RelayCommand(_ => ToggleView());
            PickKindCommand = new RelayCommand(o => PickKind(o?.ToString()));
            PickDayCommand = new RelayCommand(o => PickDay(o?.ToString()));
            TriggerBlockNowCommand = new RelayCommand(o => Console.TriggerCommercialBlockNowCommand.Execute(o),
                _ => CanOperate());
            AddSpotToBlockCommand = new RelayCommand(o => Console.AddSpotToBlockCommand.Execute(o));
            ScheduleBlockHereCommand = new RelayCommand(o => ScheduleBlockHere(o as CommercialBlock),
                _ => CanOperate() && Selected != null);

            Log_.Information("Programación: {Count} celda(s) recuperadas de {Path}",
                _schedule.ProgrammedCount, _schedule.FilePath);
        }

        /// <summary>La consola, para reutilizar sus operaciones de aire en vez de duplicarlas.</summary>
        public MainViewModel Console { get; }

        /// <summary>Las tandas que la rejilla puede programar (viven en la consola).</summary>
        public ObservableCollection<CommercialBlock> CommercialBlocks => Console.CommercialBlocks;

        /// <summary>
        /// Las 168 celdas de la vista semana. Se llenan una sola vez, en el constructor, y la
        /// colección no se toca al cambiar de vista: al vaciarla y rellenarla WPF deja de publicar los últimos contenedores
        /// en el árbol de accesibilidad (medido en vivo 2026-10-23: 21 de 24 celdas en la vista día
        /// y 165 de 168 al volver a la semana, incluso 18 s después y volviendo a resolver la raíz
        /// de UIA en cada lectura; el inventario sin filtro de tipo de control daba el mismo número,
        /// así que las celdas no estaban: las tres últimas no tenían peer). Cada vista tiene ahora
        /// su <c>ItemsControl</c> y su colección, y se alternan con <c>Visibility</c>.
        /// </summary>
        public ObservableCollection<ScheduleCellViewModel> WeekCells { get; }

        /// <summary>
        /// Las 24 horas del día elegido. Cambia de día <b>reemplazando</b> las entradas, nunca
        /// vaciando: un <c>Replace</c> le dice al generador qué contenedor concreto hay que
        /// regenerar, y un <c>Reset</c> vuelve al problema de <see cref="WeekCells"/>.
        /// </summary>
        public ObservableCollection<ScheduleCellViewModel> DayCells { get; }

        public ObservableCollection<ScheduleKindChip> KindChips { get; }

        public IReadOnlyList<string> DayHeaderLabels { get; } =
            WeeklyScheduleService.GridDayOrder.Select(WeeklyScheduleService.DayInitial).ToList();

        /// <summary>Las 24 etiquetas de la columna izquierda de la rejilla.</summary>
        public IReadOnlyList<string> HourLabels { get; } = Enumerable
            .Range(0, WeeklyScheduleService.HoursPerDay)
            .Select(h => string.Format("{0:00}:00", h))
            .ToList();

        /// <summary>Fichas D L M X J V S de la cabecera, en el orden de la rejilla.</summary>
        public IReadOnlyList<ScheduleDayChip> DayPicker { get; } = WeeklyScheduleService.GridDayOrder
            .Select(day => new ScheduleDayChip(day,
                WeeklyScheduleService.DayInitial(day),
                string.Format("ver el {0}", WeeklyScheduleService.DayName(day))))
            .ToList();

        // ---------------------------------------------------------------- modo y dia

        public bool IsWeekView
        {
            get => _isWeekView;
            private set
            {
                _isWeekView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDayView));
                OnPropertyChanged(nameof(ViewButtonText));
                OnPropertyChanged(nameof(DayTitle));
            }
        }

        public bool IsDayView => !_isWeekView;

        public string ViewButtonText => _isWeekView ? "📅 VER EL DÍA" : "🗓 VER LA SEMANA";

        public string DayTitle => _isWeekView
            ? "Semana · de lunes a domingo, las 24 horas"
            : string.Format("{0} · las 24 horas", Capitalize(WeeklyScheduleService.DayName(_selectedDay)));

        public int SelectedDay
        {
            get => _selectedDay;
            private set
            {
                _selectedDay = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DayTitle));
                RebuildDayCells();
            }
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>Elige la pauta de la celda desde las cinco fichas del editor.</summary>
        private void PickKind(string? value)
        {
            if (!int.TryParse(value, out int parsed)) return;
            EditKindValue = parsed;
            StatusText = string.Format("Pauta elegida: {0}. Pulsa APLICAR para dejarla en {1}.",
                WeeklyScheduleService.Describe(new ScheduleSlot { Kind = (ScheduleKind)parsed }),
                Selected is null ? "la celda que elijas" : SelectedTitle);
        }

        private void ToggleView()
        {
            IsWeekView = !IsWeekView;
            StatusText = IsWeekView
                ? "Vista semana: 7 columnas por 24 filas. Un clic en una celda y se edita a la derecha."
                : "Vista día: las 24 horas del día elegido, una por fila.";
        }

        private void PickDay(string? day)
        {
            // El CommandParameter llega de la XAML como texto, no como entero.
            if (!int.TryParse(day, out int value)) return;
            SelectedDay = Math.Clamp(value, 0, 6);
            if (_isWeekView) ToggleView();
        }

        /// <summary>
        /// Pone el día elegido en la rejilla de 24. Son <b>reemplazos</b>, no un vaciado: son
        /// siempre las mismas 24 posiciones, y una notificación <c>Replace</c> le dice al
        /// <c>ItemsControl</c> qué contenedor concreto hay que regenerar. Un <c>Reset</c> (Clear
        /// más Add) reconstruye los 24 contenedores y deja tres de ellos sin peer para la
        /// accesibilidad, que es lo que se midió el 2026-10-23.
        /// </summary>
        private void RebuildDayCells()
        {
            var cells = _all.Where(c => c.Day == _selectedDay).OrderBy(c => c.Hour).ToList();
            for (int i = 0; i < cells.Count; i++)
            {
                if (i >= DayCells.Count) DayCells.Add(cells[i]);
                else if (!ReferenceEquals(DayCells[i], cells[i])) DayCells[i] = cells[i];
            }
            while (DayCells.Count > cells.Count) DayCells.RemoveAt(DayCells.Count - 1);
        }

        // ---------------------------------------------------------------- cabecera

        public string ProgrammedText => string.Format("{0} de 168 celdas programadas", _schedule.ProgrammedCount);

        public string NextDueText => _schedule.NextDueText(DateTime.Now);

        public bool IsDirty => _schedule.Dirty;

        private void RaiseHeader()
        {
            OnPropertyChanged(nameof(ProgrammedText));
            OnPropertyChanged(nameof(NextDueText));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DirtyMark));
        }

        public string DirtyMark => _schedule.Dirty ? " · sin guardar" : string.Empty;

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        // ---------------------------------------------------------------- editor

        public ScheduleCellViewModel? Selected
        {
            get => _selected;
            private set
            {
                _selected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedTitle));
                OnPropertyChanged(nameof(HasClipboard));
                OnPropertyChanged(nameof(ClipboardText));
                ApplyEditFromSelection();
            }
        }

        public string SelectedTitle => Selected is null
            ? "ninguna celda elegida"
            : string.Format("{0} · {1:00}:00", Capitalize(Selected.DayName), Selected.Hour);

        /// <summary>La celda copiada, para el pegado y el relleno de rango.</summary>
        public ScheduleCellViewModel? ClipboardCell
        {
            get => _clipboard;
            private set
            {
                _clipboard = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasClipboard));
                OnPropertyChanged(nameof(ClipboardText));
            }
        }

        public bool HasClipboard => ClipboardCell != null;

        public string ClipboardText => ClipboardCell is null
            ? "sin nada copiado"
            : string.Format("{0} {1:00}:00 · {2}", Capitalize(ClipboardCell.DayName), ClipboardCell.Hour,
                WeeklyScheduleService.Describe(ClipboardCell.Slot));

        public ScheduleKind EditKind
        {
            get => _editKind;
            set
            {
                _editKind = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EditKindValue));
                OnPropertyChanged(nameof(ShowRefPicker));
                OnPropertyChanged(nameof(RefHint));
                RefreshKindChips();
            }
        }

        /// <summary>Marca qué ficha de pauta es la que está escrita en el editor.</summary>
        private void RefreshKindChips()
        {
            foreach (var chip in KindChips)
            {
                chip.IsCurrent = chip.Value == (int)_editKind;
            }
        }

        /// <summary>Las fichas de pauta pasan un entero: el enum como <c>int</c> evita el conversor.</summary>
        public int EditKindValue
        {
            get => (int)_editKind;
            set { EditKind = (ScheduleKind)Math.Clamp(value, 0, 4); }
        }

        public bool ShowRefPicker => _editKind is ScheduleKind.Cart or ScheduleKind.Tanda;

        public int EditMinute
        {
            get => _editMinute;
            set { _editMinute = Math.Clamp(value, 0, 59); OnPropertyChanged(); }
        }

        public string EditRef
        {
            get => _editRef;
            set { _editRef = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(RefHint)); }
        }

        public bool EditEnabled
        {
            get => _editEnabled;
            set { _editEnabled = value; OnPropertyChanged(); }
        }

        public int RangeEndHour
        {
            get => _rangeEndHour;
            set { _rangeEndHour = Math.Clamp(value, 0, 23); OnPropertyChanged(); }
        }

        /// <summary>
        /// Qué es lo que pone en Referencia, resuelto contra lo que hay ahora mismo en la emisora.
        /// El desplegable que había aquí se cayó porque el identificador de una tanda no lo escribe
        /// nadie a mano y el <c>Popup</c> de un ComboBox de WPF vive en su propio HWND: ni el
        /// operador ni una prueba de interfaz lo manejan bien. La referencia de un cart se teclea
        /// (F1 a F12) y la de una tanda se elige con el botón «PONER EN LA CELDA» de la lista.
        /// </summary>
        public string RefHint => _editKind switch
        {
            ScheduleKind.Cart => string.IsNullOrWhiteSpace(_editRef)
                ? "escribe el atajo del pad: F1 a F12"
                : DescribeCart(_editRef),
            ScheduleKind.Tanda => string.IsNullOrWhiteSpace(_editRef)
                ? "elige la tanda abajo con «🗓 PONER EN LA CELDA»"
                : DescribeBlock(_editRef),
            _ => string.Empty
        };

        private string DescribeCart(string reference)
        {
            var pad = Console.CartWallViewModel.CartPads.FirstOrDefault(
                p => string.Equals(p.ShortcutText, reference.Trim(), StringComparison.OrdinalIgnoreCase));
            return pad is null
                ? "no hay ningún pad de la botonera con ese atajo"
                : string.Format("{0} · {1}{2}", pad.ShortcutText, pad.Title, pad.HasAudio ? "" : " (sin archivo)");
        }

        private string DescribeBlock(string reference)
        {
            var block = CommercialBlocks.FirstOrDefault(
                b => string.Equals(b.BlockId, reference.Trim(), StringComparison.OrdinalIgnoreCase));
            return block is null
                ? "no hay ninguna tanda con ese identificador"
                : string.Format("{0} · a los :{1:00} · {2} cuña(s)", block.Name, block.TriggerMinute, block.Spots.Count);
        }

        private void ApplyEditFromSelection()
        {
            var slot = Selected?.Slot;
            if (slot is null)
            {
                EditKind = ScheduleKind.Hora;
                EditMinute = 0;
                EditRef = string.Empty;
                EditEnabled = true;
                return;
            }

            _editKind = slot.Kind;
            OnPropertyChanged(nameof(EditKind));
            OnPropertyChanged(nameof(EditKindValue));
            OnPropertyChanged(nameof(ShowRefPicker));
            RefreshKindChips();
            EditMinute = slot.Minute;
            EditRef = slot.Ref;
            EditEnabled = slot.Enabled;
            OnPropertyChanged(nameof(RefHint));
        }

        private static bool CanOperate() => App.Auth.IsAuthorized(UserRole.Operator);

        private void SelectCell(ScheduleCellViewModel? cell)
        {
            if (cell is null) return;
            if (_selected != null) _selected.IsSelected = false;
            Selected = cell;
            cell.IsSelected = true;
        }

        private ScheduleSlot Draft() => new()
        {
            Kind = EditKind,
            Minute = EditMinute,
            Ref = ShowRefPicker ? EditRef : string.Empty,
            Enabled = EditEnabled
        };

        /// <summary>
        /// El borrador del editor, pero solo si se puede cumplir: un cart o una tanda sin
        /// referencia es una celda que a su hora no va a sonar. Mejor avisar y no escribirlo.
        /// </summary>
        private ScheduleSlot? DraftOrWarn()
        {
            var draft = Draft();
            if (draft.Kind is not (ScheduleKind.Cart or ScheduleKind.Tanda) || !string.IsNullOrWhiteSpace(draft.Ref))
                return draft;

            StatusText = draft.Kind == ScheduleKind.Cart
                ? "Falta la referencia: escribe el atajo del pad (F1 a F12)."
                : "Falta la referencia: elige la tanda en la lista de abajo con «🗓 PONER EN LA CELDA».";
            return null;
        }

        private void ApplyCell()
        {
            if (Selected is null) return;
            if (DraftOrWarn() is not { } draft) return;
            _schedule.Set(Selected.Day, Selected.Hour, draft);
            Selected.Refresh(_schedule.At(Selected.Day, Selected.Hour));
            RaiseHeader();
            // El minuto se escribe con «:00», no con «:02». En un formato numerico personalizado el
            // unico digito de relleno es el 0 y un 2 suelto es un literal, asi que «{0:02}» con el
            // minuto 47 devolvia «472»: medido en vivo 2026-10-23, la celda guardaba 47 y sonaba a
            // las 07:47 mientras el estado ponia «a los :472».
            StatusText = string.Format("{0} {1:00}:00 -> {2}{3}", Capitalize(Selected.DayName), Selected.Hour,
                WeeklyScheduleService.Describe(draft), draft.IsEmpty ? "" : string.Format(" a los :{0:00}", draft.Minute));
            Log_.Information("Pauta editada: {Day} {Hour:00} -> {What}", Selected.DayName, Selected.Hour,
                WeeklyScheduleService.Describe(draft));
        }

        private void ClearCell()
        {
            if (Selected is null) return;
            _schedule.Set(Selected.Day, Selected.Hour, null);
            Selected.Refresh(null);
            RaiseHeader();
            StatusText = string.Format("{0} {1:00}:00 libre.", Capitalize(Selected.DayName), Selected.Hour);
        }

        /// <summary>
        /// Rellena el rango del día elegido desde la hora de la celda activa hasta
        /// <see cref="RangeEndHour">Hasta</see>, ambos incluidos. Es lo que se hace en cabina
        /// cuando una pauta va «de 06:00 a 18:00»: veinticuatro clics no.
        /// </summary>
        private void FillRange()
        {
            if (Selected is null) return;
            if (DraftOrWarn() is not { } draft) return;
            int from = Math.Min(Selected.Hour, RangeEndHour);
            int to = Math.Max(Selected.Hour, RangeEndHour);
            int applied = 0;

            for (int hour = from; hour <= to; hour++)
            {
                _schedule.Set(Selected.Day, hour, draft);
                applied++;
            }

            RefreshAll();
            RaiseHeader();
            StatusText = string.Format("{0}: {1} hora(s) de {2:00}:00 a {3:00}:00 con {4}.",
                Capitalize(Selected.DayName), applied, from, to, WeeklyScheduleService.Describe(draft));
        }

        /// <summary>La misma pauta a la misma hora los siete días (el caso típico: la hora hablada).</summary>
        private void ApplyToWeek()
        {
            if (Selected is null) return;
            if (DraftOrWarn() is not { } draft) return;
            foreach (int day in WeeklyScheduleService.GridDayOrder)
            {
                _schedule.Set(day, Selected.Hour, draft);
            }

            RefreshAll();
            RaiseHeader();
            StatusText = string.Format("Los 7 días a las {0:00}:00 -> {1}.", Selected.Hour,
                WeeklyScheduleService.Describe(draft));
        }

        private void CopyCell()
        {
            if (Selected?.Slot is null) return;
            ClipboardCell = Selected;
            StatusText = "Copiada " + ClipboardText + ". Elige la celda destino y pulsa PEGAR.";
        }

        private void PasteCell()
        {
            if (Selected is null || ClipboardCell?.Slot is not { } source) return;
            _schedule.Set(Selected.Day, Selected.Hour, source);
            Selected.Refresh(_schedule.At(Selected.Day, Selected.Hour));
            RaiseHeader();
            StatusText = string.Format("Pegada en {0} {1:00}:00: {2}.", Capitalize(Selected.DayName), Selected.Hour,
                WeeklyScheduleService.Describe(source));
        }

        /// <summary>
        /// Programa la tanda en la celda elegida, en el minuto que la tanda ya tiene.
        /// Es el atajo que faltaba: sin él, dejar una tanda en la rejilla obligaba a
        /// copiar su identificador del desplegable, y ese identificador no se escribe a mano.
        /// </summary>
        private void ScheduleBlockHere(CommercialBlock? block)
        {
            if (block is null || Selected is null) return;

            EditKind = ScheduleKind.Tanda;
            EditRef = block.BlockId;
            EditMinute = Math.Clamp(block.TriggerMinute, 0, 59);
            EditEnabled = true;
            ApplyCell();
            StatusText = string.Format("{0} {1:00}:00 -> tanda «{2}» a los :{3:00}.",
                Capitalize(Selected.DayName), Selected.Hour, block.Name, EditMinute);
        }

        private void Save()
        {
            if (!AuthGate.Admin()) return;
            StatusText = _schedule.Save();
            RaiseHeader();
        }

        private void Reload()
        {
            StatusText = _schedule.Load();
            RefreshAll();
            RaiseHeader();
        }

        // ---------------------------------------------------------------- rejilla

        private void RefreshAll()
        {
            foreach (var cell in _all)
            {
                cell.Refresh(_schedule.At(cell.Day, cell.Hour));
            }
        }

        /// <summary>Pinta en la rejilla la hora en la que está el reloj.</summary>
        private void MarkNow()
        {
            var now = DateTime.Now;
            int day = (int)now.DayOfWeek;
            int hour = now.Hour;
            var cell = _all.FirstOrDefault(c => c.Day == day && c.Hour == hour);
            if (ReferenceEquals(cell, _nowCell)) return;

            if (_nowCell != null) _nowCell.IsNow = false;
            _nowCell = cell;
            if (_nowCell != null) _nowCell.IsNow = true;
        }

        /// <summary>
        /// Lo que la consola pide al cerrar el módulo: que se guarden los cambios sin cuadro
        /// de diálogo. Si no hay sesión de administrador no se pregunta: se avisa en el estado
        /// y la rejilla sigue en memoria hasta cerrar el programa.
        /// </summary>
        public string SaveIfDirty()
        {
            if (!_schedule.Dirty) return string.Empty;
            if (!App.Auth.IsAuthorized(UserRole.Admin))
            {
                StatusText = "La programación cambió pero no se guardó: hace falta una sesión de "
                    + "administrador (botón GUARDAR PROGRAMACIÓN).";
                return StatusText;
            }

            StatusText = _schedule.Save();
            RaiseHeader();
            return StatusText;
        }

        // ---------------------------------------------------------------- comandos

        public ICommand SelectCellCommand { get; }
        public ICommand ApplyCellCommand { get; }
        public ICommand ClearCellCommand { get; }
        public ICommand FillRangeCommand { get; }
        public ICommand ApplyToWeekCommand { get; }
        public ICommand CopyCellCommand { get; }
        public ICommand PasteCellCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand ToggleViewCommand { get; }
        public ICommand PickKindCommand { get; }
        public ICommand PickDayCommand { get; }
        public ICommand TriggerBlockNowCommand { get; }
        public ICommand AddSpotToBlockCommand { get; }
        public ICommand ScheduleBlockHereCommand { get; }
    }
}
