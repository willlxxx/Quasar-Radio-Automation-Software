# Plan de mejora de la interfaz — Quasar Radio Pro

**Fecha:** 2026-10-22 · **Estado:** propuesta para aprobación · **Autor:** revisión de la UI actual
**Compañero:** [`PLAN_MEJORA_ARQUITECTURA.md`](PLAN_MEJORA_ARQUITECTURA.md) — ese plan mueve lógica; **este mueve la superficie**. Son independientes y este se puede ejecutar antes, pero hay dos puntos de contacto obligatorios al final (§7).

El pedido del fundador, textual: *"una mejora sustancial de interfaz del software con estándar de calidad moderno y profesional, que los módulos se ejecuten en ventanas separadas (Biblioteca, Streaming, configuración, DSP, etc.), tomando como ejemplo Radio Boss pero sin su interfaz característica"*.

Este documento traduce eso a decisiones concretas sobre el código que hay hoy.

---

## 1. Inventario real de la interfaz

Medido sobre los archivos, no recordado:

| Superficie | Tamaño | Naturaleza |
|---|---|---|
| `MainWindow.xaml` | 1.680 líneas · 128 KB | consola + 8 pestañas de "SUITE v3.0" apiladas en un solo archivo |
| `LibraryManagementWindow.xaml` | 728 líneas | única ventana de módulo que existe hoy |
| `LoginWindow` / `UserManagementWindow` / `TrackEditorDialog` | 31 / 35 / 70 líneas | diálogos funcionales, sin estilo compartido |
| `App.xaml` | 8 líneas | **`Application.Resources` vacío**: no hay un solo recurso compartido en toda la aplicación |
| `MainViewModel.cs` | 727 líneas | fachada con ~60 propiedades de reenvío "para compatibilidad con XAML existente" |
| Sub-ViewModels | 714 + 153 + 155 + 110 + 120 | `AudioPlayer`, `CartWall`, `Library`, `Streaming`, `Theme` — creados con `new` dentro de `MainViewModel` |

Detalles que mandan el diseño:

- La suite actual son **8 pestañas dentro de la ventana principal**: BASE DE DATOS (1161), PROCESADOR DSP (1204, `IsEnabled="{Binding IsAdmin}"`), CARTWALL SFX (1259), AUX PFL (1335, `IsEnabled="{Binding CanOperate}"`), TANDAS (1381), STREAMING (1431), GRABACIÓN (1527), ESTADÍSTICAS (1597).
- Encima hay una segunda capa de UI real: header/barra de estudio (186–326), Deck A (335–644), Deck B (645–954), master + VU (955–1028) y rejilla de emisión con `DataGrid` arrastrable (1036–1133). **Lo que está al aire y lo que es configuración compiten por el mismo metro cuadrado.**
- `F1`–`F12` están en `Window.InputBindings` de `MainWindow.xaml:9-22` → `CartWallViewModel.TriggerCartPadCommand`.
- Frecuencia de colores dentro de `MainWindow.xaml`: `#8A99AD` 42 veces, `#00E5FF` 38, `#FFFFFF` 34, `#262D3D` 26, `#00E676` 26, `#0B0D14` 18, `#A855F7` 17… **328 literales hexadecimales, 62 valores distintos.**
- **0** usos de `DynamicResource` en `MainWindow.xaml`.

## 2. Diagnóstico

**I1 — El selector de tema es decorativo.** `ThemeViewModel.ApplyTheme()` (líneas 78–101) fija tres cadenas (`WindowBackground`, `CardBackground`, `AccentColor`). esas cadenas están enlazadas **7 veces en toda la aplicación** (4 · 1 · 2) y las otras cuatro ventanas tienen **0** enlaces. Cambiar de "Cyan Broadcast" a "Charcoal Pro" hoy cambia el fondo de la ventana principal y nada más. Un sistema de temas real no se puede construir sobre enlaces de propiedades: necesita recursos.

**I2 — No hay sistema visual, hay 53 plantillas duplicadas.** Los 8 estilos de `Window.Resources` (24–176) conviven con ~45 estilos y plantillas escritos en línea repartidos por el archivo (el `Style` de `TabItem` se reescribe dentro de `TabControl.Resources` en 1136–1159). Cada control nuevo copia y pega. Con 328 hexes sueltos, rediseñar significa buscar y reemplazar a ciegas.

**I3 — La consola está un piso por debajo de lo que necesita una emisora.** La botonera F1–F12 vive en la pestaña 3, así que **si tienes abierta la Biblioteca, la botonera no se ve** (el atajo sigue funcionando porque el binding cuelga de la `DataContext` de la ventana). El estado de emisión (cuándo entra la tanda, si el stream cayó, si hay silencio) está repartido entre el header, el footer y una pestaña que puede estar oculta.

**I4 — Dividir en ventanas sin preparar el terreno rompe tres cosas:**
1. **Los atajos**: `InputBindings` es por ventana. Si F1–F12 quedan solo en la consola, el operador pulsando F5 sobre la ventana de Biblioteca no dispara nada.
2. **El estado**: hoy `AudioPlayerViewModel` y compañía los crea `new` dentro de `MainViewModel`, así que la consola es la única dueña. Dos ventanas que necesiten el mismo sub-VM tendrían dos instancias con dos vistas del mismo motor.
3. **El apagado**: `App.xaml.cs:60-69` usa `OnExplicitShutdown` → `OnMainWindowClose`. Si una ventana de módulo se vuelve `MainWindow` o se cierra mal, la aplicación se apaga **con emisión en curso**.

**I5 — Accesibilidad y densidad sin criterio.** El footer usa `#55657B` sobre `#0B0D14`: contraste calculado ≈ **3,2:1**, por debajo del 4,5:1 mínimo para texto legible. El secundario `#8A99AD` sobre `#0F111A` sí llega (≈6,5:1). No hay regla de contraste; hay costumbre. Tampoco hay control de densidad ni tipografía monoespaciada para el reloj de estudio, que hoy **baila** cuando cambian los dígitos.

**I6 — Ventanas ya existentes que nadie gobierna.** `LibraryManagementWindow` es una ventana real con 87 enlaces y 728 líneas, abierta con `new LibraryManagementWindow(...)` desde `ViewModels/LibraryViewModel.cs:144-148`, sin posición recordada, sin instancia única y con un `App.Services?.GetService<AudioDatabaseService>()` de respaldo en `LibraryManagementViewModel.cs:202`. Es el patrón a corregir, no a repetir.

## 3. Principio de diseño

> **La consola es la cabina. Los módulos son las salas.**

Reglas que de ahí se derivan y que deciden cualquier duda de diseño:

1. **En la consola solo hay lo que se necesita mientras se está al aire**: decks, master/VU, playlist, botonera, micrófono, reloj y estado. Todo lo demás se va.
2. **Nada que requiera concentración sostenida puede vivir en la consola**: editar metadatos, configurar el compresor o revisar estadísticas son tareas de ventana aparte, que se pueden llevar a un segundo monitor.
3. **Cerrar un módulo nunca apaga la emisión.** La consola es la única ventana dueña del ciclo de vida de la aplicación.
4. **Referencia Radio Boss = flujo de trabajo, no estética.** Copio la separación funcional (media library / scheduler / streams / cart wall / options) y la disciplina de "una pantalla por tarea". **No** copio su paleta, su iconografía, sus pestañas ni su rejilla beige. La identidad sigue siendo Quasar: oscura, con acentos neón, pero ordenada.

## 4. Mapa de ventanas (propuesta)

### 4.1 Consola al Aire — `MainWindow` rediseñada, siempre abierta

```
┌─ barra superior ────────────────────────────────────────────────┐
│ logo · QUASAR RADIO  [AL AIRE][MIC][STREAM]  tanda 00:47  AUTO  │  ← estado, no decoración
├─ decks ───────────────────────────────┬─ master ────────────────┤
│ DECK A  título / progreso / cue / BPM │  VU + silencio + volumen│
│ DECK B  título / progreso / cue / BPM │  MIC ducking            │
├─ lista de reproducción ──────────────┴──────────────────────────┤
│ próximo · en aire · historial reciente  (DataGrid compacto)     │
├─ botonera F1–F12 (franja fija, 6×2, con nombre visible) ────────┤
├─ módulos: [Biblioteca][Programación][DSP][Streaming][Config]… ─┤
└─────────────────────────────────────────────────────────────────┘
```

- La botonera deja de ser pestaña y pasa a franja fija: es lo más pulsado del aire.
- El reloj de estudio y el countdown de tanda pasan a bloque de estado con dígitos monoespaciados.
- Los 8 temas de la suite se convierten en botones de módulo en la última franja, con indicador de "abierto".
- Objetivo: **1.680 líneas → ≤ 350**.

### 4.2 Biblioteca de medios (reemplaza y moderniza `LibraryManagementWindow`)

Árbol de categorías (música, jingles, separadores, voces, comerciales, horarias) + búsqueda avanzada sobre `AdvancedSearchService` (477 líneas) y `AudioAnalysisService` (186), vista de columnas con duración real, editor de metadatos en lote (`TrackEditorService`), detección de archivos faltantes o ilegibles, y regeneración de la auto-playlist (`SmartRotationEngine`). Es el módulo de mayor uso: se diseña primero. **Ventana sin dueño modal**, para poder tenerla en el monitor de al lado mientras la consola está al aire.

### 4.3 Programación

Rejilla horaria editable (lo que hoy sirve `CustomTimeEngine` con `HRS{hh}_O` / `MIN{mm}`), edición de bloques comerciales (`CommercialScheduler`, `CommercialBlock`) y reglas de rotación. Hoy eso está repartido entre TANDAS (pestaña 5) y la cabecera; aquí se unifica en una vista de día/semana.

### 4.4 Procesador DSP

EQ gráfico de 10 bandas con arrastre directo sobre la curva (además de los deslizadores actuales), AGC, compresor/limitador con presets, y **espectro en vivo** alimentado por la FFT de 2048 puntos que `BroadcastSampleProvider` ya calcula. Solo Admin, y el permiso se re-evalúa **al abrir la ventana**, no solo al enlazar `IsEnabled`.

### 4.5 Streaming y Grabación

Configuración Icecast (servidor, puerto, mountpoint, password, ffmpeg), salud del enlace con el circuito closed/open/half-open visible, métricas (bitrate, conexión, descartes, underruns), exportación nowplaying (RDS/TXT/JSON) y la grabadora con su listado de archivos. Dos pestañas de hoy (1431, 1527) que son el mismo dominio.

### 4.6 Configuración

Agrupado por secciones navegables: **Estación** (nombre, logo, RDS), **Hardware de Audio** (dispositivos de entrada/salida, backend WASAPI/WaveOut, canales), **Seguridad** (usuarios y roles, acceso a `UserManagementWindow` como sub-diálogo), **Rutas de datos** (la carpeta de la librería real, ver §7), **Hora y locución** (`TimeAnnouncementService`), **Apariencia** (tema, densidad, tamaño de fuente) y **Atajos de teclado**.

### 4.7 Reportes y estadísticas

Lo de la pestaña ESTADÍSTICAS (1597) más el exportador `reporte_emision_*.txt`, con filtros por rango de fechas. Se separa porque es análisis, no operación.

### 4.8 Diálogos (hijos, modales, con `Owner`)

`LoginWindow`, `UserManagementWindow`, `TrackEditorDialog`. Siguen siendo modales: son decisiones puntuales, no espacios de trabajo.

## 5. Sistema visual

**Tokens primero, pantallas después.** Sin esto, cada ventana nueva vuelve a hardcodear colores y el plan entero no sirve.

**5.1 `Themes/Tokens.xaml`** — diccionario de recursos con nombre semántico, no cromático:

```
Color.PrimaryBackground / Surface / SurfaceRaised / Border / BorderStrong
Color.TextPrimary / TextSecondary / TextMuted / TextOnAccent
Color.Accent / AccentSuccess / AccentWarn / AccentDanger / AccentInfo
Brush.* homólogos (SolidColorBrush sobre cada Color)
Space.1..6 · Radius.Sm/Md/Lg · Font.Xs..Xl · Weight.*
Semantic.OnAir / Live / Standby / Muted / Fading
```

Las vistas **solo** usan `DynamicResource`. Un hex en una vista se convierte en error de revisión. Con esto el tema pasa de 7 enlaces a gobernar los ~328 sitios sin tocar una línea de código.

**5.2 `Themes/Controls.xaml`** — estilos implícitos (sin `x:Key`, para que apliquen solos) sobre: `Button`, `ToggleButton`, `RepeatButton`, `Slider` (con burbuja de valor), `TextBox`, `ComboBox`, `DataGrid` + `DataGridColumnHeader` + fila, `TabItem` como navegación segmentada, `ListBox`, `ProgressBar`, `ToolTip`, `Border` `Card` explícito y `SectionHeader`. Las 8 plantillas de `Window.Resources` y las ~45 en línea se retiran a aquí.

**5.3 Cuatro temas como diccionarios** (`StudioDarkNeon`, `CyanBroadcast`, `CharcoalPro`, `DeepNavyFM`) que solo redefinen colores. `ThemeViewModel.ApplyTheme()` cambia a `Application.Current.Resources.MergedDictionaries` (reemplazo del diccionario de tokens), **no** propiedades enlazadas. El logo de estación se queda en el VM.

**5.4 Tipografía y render.** `Segoe UI` para interfaz; pila `Cascadia Code, Consolas` monoespaciada con `Typography` de dígitos tabulares para reloj, BPM, tiempos y VU-numéricos (evita el salto de ancho). `UseLayoutRounding="True"`, `TextOptions.TextFormattingMode="Display"` y `SnapsToDevicePixels` en los bordes de 1 px, que sin redondeo se ven grises a DPI 125/150. Tamaños mínimos: 11 px solo en etiquetas del VU, 12 px cuerpo, 13 px énfasis.

**5.5 Iconos por glifo.** `Segoe Fluent Icons` / `Segoe MDL2 Assets` (viene con Windows, sin dependencia ni assets binarios) para play/pause/cue/on-air/settings. Botones que hoy dicen "PAUSA" en texto pasan a icono + tooltip, con etiqueta de texto conservada donde la ambigüedad cuesta un error de aire.

**5.6 Densidad y pantalla base.** Diseño de referencia 1.440 × 900 (la actual). Dos modos: **Compacta** (mesa de control, más filas visibles) y **Cómoda** (operación sentado). Multi-monitor soportado de serie: la consola en uno, los módulos arrastrables al otro, con tamaño recordado.

**5.7 Reglas de estado y contraste.** Todo texto ≥ 4,5:1; todo indicador crítico (AL AIRE, silencio, circuito abierto) ≥ 7:1 y **nunca solo color** — icono o texto además, para monitores de cabina lavados y daltónicos. Estados de grabación y on-air con señal clara de entrada/salida.

## 6. Navegación y ciclo de vida

**6.1 `IModuleNavigator`** (capa de presentación, singleton en DI):

```csharp
public enum ModuleKey { Library, Scheduler, Dsp, Streaming, Settings, Reports }
void Open(ModuleKey key);       // si ya está abierta: activar, no duplicar
bool IsOpen(ModuleKey key);     // para el indicador de la barra de módulos
void Toggle(ModuleKey key);
```

Implementación: mapa `ModuleKey → (singleton de ventana, rect guardado)`. Las ventanas se resuelven desde `IServiceProvider`, nunca con `new`. `Closing` cancelado → `Hide()` (un módulo cerrado a mitad de tarea no pierde estado). Al cerrar de verdad, se guarda geometría.

**6.2 Instancia única por módulo.** `AudioPlayerViewModel`, `CartWallViewModel`, `LibraryViewModel`, `StreamingViewModel`, `ThemeViewModel` y `MainViewModel` pasan de `AddTransient` a **`AddSingleton`** en `ConfigureServices`, y `MainViewModel` deja de crearlos con `new` para recibirlos inyectados. Sin este cambio, dos ventanas muestran dos verdades del mismo motor.

**6.3 Desaparición de la fachada.** Los ~60 reenvíos de `MainViewModel` (líneas 36–80 y siguientes) se eliminan: cada vista de módulo enlaza **directamente** a su VM (`DataContext` resuelto por `DataTemplate` por tipo de VM, para no nombrar ventanas desde código). Solo se conservan como *shim* durante la transición y se borran al final de cada fase.

**6.4 Atajos globales.** Los `KeyBinding` F1–F12 se declaran una sola vez como recurso en `App.xaml` (`InputBindingCollection`) y se asignan en el constructor de **cada** ventana, para que la botonera funcione con el foco puesto en Biblioteca o DSP. Los atajos por módulo (Ctrl+F buscar, Supr quitar de playlist) viven en el diccionario de la ventana correspondiente.

**6.5 Seguridad de apagado.** Se mantiene `ShutdownMode.OnMainWindowClose` con la consola como `MainWindow`, y se añade en su `Closing`: si `IsPlaying`, hay stream activo o grabación en curso, confirmar. Ninguna ventana de módulo puede cerrar la aplicación. `Application.Exit` hace dispose ordenado de motores (stop de NAudio, cierre de Icecast, flush de logs).

**6.6 Geometría recordada.** `layout.json` en la carpeta de datos de la emisora (no en `bin/`), con posición/tamaño/estado por módulo, escritura con debounce y a través del `AuthService`-like de escritura atómica (`.tmp` + `File.Move`) que sí funciona hoy.

## 7. Puntos de contacto obligatorios con el plan de arquitectura

| Contacto | Qué hacer |
|---|---|
| **Reloj de emisión (`DispatcherTimer`)** | **No se toca en este plan.** Los `DispatcherTimer` de `AudioEngine.cs:152-162` siguen en el hilo UI. Al sacar el DSP/Streaming a ventanas separadas hay más ventanas consumiendo tick de UI, así que **la Fase 3 del plan de arquitectura (sacar el reloj del hilo UI) debe hacerse antes o justo después de la UI Fase 4**, nunca después de multiplicar ventanas sin haberlo comprobado con RUTINA_HUMO. |
| **`DataPaths` / Fase 2.5** | El módulo Configuración → "Rutas de datos" es la **interfaz** de ese cambio. No tiene sentido abrir una ventana de configuración que siga apuntando la librería a `bin/`. UI Fase 6 se alinea con Fase 2.5. |

## 8. Fases

**UI-0 · Congelado (0 riesgo).** Capturas de la consola actual en los 4 temas, y lista de las 8 pestañas con sus comandos y enlaces. Sirve de referencia visual para no perder funciones por el camino. *Salida: `docs/ui-baseline/`.*

**UI-1 · Sistema de diseño (sin cambios de layout).** ✅ **Entregado 2026-10-22.** `Tokens.xaml` (100 claves), `Controls.xaml`, 4 temas de diccionario, `ThemeService` con `appearance.json`, y 0 literales hex en todos los `.xaml` fuera de `Themes/`. *Aceptación: cambiar de tema afecta a **todas** las ventanas y controles; 0 hex en vistas nuevas; la app se ve igual que antes pero gobernada.* — Cumplida en dos ventanas (consola y `LoginWindow`, cuatro capturas cada una); ver §4 de `docs/UI_BASELINE.md`. **Queda una comprobación abierta:** el repintado *sin reiniciar* no se pudo ejercitar porque el selector de tema está atado a `IsAdmin` y la consola arranca sin sesión (hallazgo H1). Se cierra cuando él inicie sesión con su cuenta `admin`.

**UI-2 · Navegación y ciclo de vida.** ✅ **Entregado 2026-10-22** (estructura; las ventanas nuevas la irán ocupando en UI-4/UI-5). `ModuleNavigator` con `Open/IsOpen/Toggle/EnsureAdmin` + `ModuleClosed`, ventanas de módulo transient y **no modales**, una sola instancia por módulo, geometría en `%APPDATA%\QuasarRadio\layout.json` con escritura atómica y validación multi-monitor, `StudioKeys.CartWall` para F1–F12 en cualquier ventana, y `OnClosing` protegido en la consola. *Aceptación: abrir Biblioteca dos veces no crea una segunda (✅ por código: `Open` reactiva la existente); cerrar DSP no corta el aire (✅ la aplicación solo se apaga con la consola, y pregunta si hay aire); F1–F12 funcionan con foco en cualquier ventana (⚠️ pendiente de pulsación real: disparar un pad suena en cabina y no lo hago sin él delante).* Detalle y evidencia en §5 de `docs/UI_BASELINE.md`. **Dos condicionantes nuevos, medidos en UI-1:** (a) hoy `LoginWindow` y `UserManagementWindow` se abren con `ShowDialog()` (`MainWindow.xaml.cs:34,44`) y el plan pide ventanas de módulo **no modales** — hay que decidir el trato de los diálogos; (b) las cuentas existen (`admin`, `willlxxx`) pero la consola arranca **sin sesión**, así que los controles gated por `IsAdmin` nacen apagados: el operador ve un botón LIBRERÍA muerto y un selector de tema inservible. UI-2 convierte esa pared en una puerta: el comando siempre es ejecutable y `IModuleNavigator.EnsureAdmin()` pide el acceso al abrir, que es lo que ya hacía el botón `USUARIOS`.

**UI-3a · Layout de la consola actual.** ✅ **Entregado 2026-10-22** (corte previo a la reescritura, para no dejar a un operador emitiendo sin ver su nivel de salida). D1 resuelto: la fila de decks pasa a `Auto`/`MinHeight=210` y el VU master se pinta (medido: etiqueta en `1016,210` y dos barras `180x20`, también maximizada). D2 resuelto con `WindowFitting.cs`: la consola se encaja en el **área útil** del monitor que la recibe (`80,0 1440x860` en el panel 1600 × 900, barra de título dentro) y solo se mueve si hace falta, para no pisarle la geometría a `ModuleNavigator`. D3 contenido con `WrapPanel` en BASE DE DATOS y ESTADÍSTICAS, encabezados de `Duración`/`Género` con hueco, `TrimmingCell` (puntos suspensivos + tooltip) en las dos tablas, y el pie de ventana re-organizado en `Grid`. *Aceptación: las 4 comprobaciones de `scripts/ui/verificar_consola.ps1` en True y `RESTANTES=0` en las 5 ejecuciones.* Ver §6 de `docs/UI_BASELINE.md`. **No entra en esta fase:** D4 (logo ilegible), D5 (pestañas en dos hileras) y el parche de `WrapPanel`, que se borra cuando esas funciones tengan su ventana en UI-4/UI-5.

**UI-3b · Consola al aire.** Layout nuevo de la §4.1, franja de módulos, botonera fija, barra de estado. Se retiran las 8 pestañas. *Aceptación: la consola no contiene DSP, Streaming, Grabación ni Estadísticas; ≤ 350 líneas.*

**UI-3b · paso 1 · las ventanas antes de quitar las pestañas.** ✅ **Entregado 2026-10-22.** `ScheduleWindow`, `DspWindow`, `StreamingWindow` y `ReportsWindow` reciben el XAML de sus pestañas (traslado casi literal, `DataContext` = el `MainViewModel` singleton), se registran transient en DI y se abren por `ModuleNavigator`, que ahora aplica la puerta de administrador dentro de `Open()` (`RequiresAdmin`) en lugar de fiarse del `IsEnabled` de la pestaña. La consola gana una **franja de módulos** sobre el pie con indicador de ventana abierta y pierde el botón duplicado de LIBRERÍA. De paso revive lo que estaba muerto en GRABACIÓN: `RecordingFiles` ya se rellena y ELIMINAR recibe el archivo seleccionado. Verificado en vivo en las 4 ventanas no bloqueantes + la puerta de DSP en `docs/UI_BASELINE.md` §7. **Las pestañas siguen puestas a propósito**: el paso 2 las retira cuando lo que queda en la consola (botonera fija, decks, rejilla, BASE DE DATOS hasta UI-4) esté cerrado. *Aceptación del paso 1: cada dominio movido abre su ventana, recuerda geometría y se cierra sin apagar la emisora — cumplido.*

**UI-4 · Biblioteca y Programación** (los dos de mayor uso, y donde más se justifica el monitor secundario). *Aceptación: editar metadatos en lote y re-generar auto-playlist sin salir de Biblioteca; rejilla horaria editable con vista día/semana.*

**UI-5 · DSP, Streaming/Grabación, Configuración, Reportes.** *Aceptación: DSP con espectro en vivo y permisos re-evaluados al abrir; Configuración con secciones navegables y rutas funcionando.*

**UI-6 · Ajuste fino.** Contraste, densidad, multi-monitor, atajos, manual de operador, y RUTINA_HUMO ampliada a las ventanas nuevas (pasos 3–8 pendientes se ejecutan aquí). *Aceptación: rutina de humo completa superada por el fundador en la cabina.*

**Desviación de la §6.3/§6.4, declarada:** en vez de resolver la vista con `DataTemplate` por tipo de VM, el navegador nombra la ventana en `ModuleNavigator.Create`. Sale igual de limpio desde el ViewModel (ningún VM hace ya `new XWindow()`: quedan 0 construcciones de ventana fuera de `App` y del propio navegador) y evita el registro de plantillas hasta que existan las 6 ventanas. Si en UI-4 se prefiere el `DataTemplate`, es cambiar ese `switch` y nada más.

Cada fase se entrega con `dotnet build quasar_radio.sln` en 0 errores / 0 advertencias y **cero escritas en `bin/` desde los fixtures** (regla del incidente del 2026-10-22).

## 9. Decisiones adoptadas (reversibles con decírtelo)

1. **Sistema visual propio con tokens XAML, sin librería de componentes de terceros.** Alternativa considerada: `WPF-UI` (Fluent) o HandyControl, que dan acabado moderno rápido. La descarto porque mete una dependencia externa en una app de emisión que funciona offline, porque el control fino de densidad/tamaños que pide una cabina es limitado, y porque no resuelve los 328 hex: sigue haciendo falta la capa de tokens. *Si prefieres acabado Fluent ya y aceptas la dependencia, UI-1 cambia de implementation a paquete y el resto del plan sigue igual.*
2. **6 ventanas de módulo + 3 diálogos; la consola sin pestañas** (Biblioteca, Programación, DSP, Streaming y Grabación, Configuración, Reportes).
3. **Sub-ViewModels a singleton en DI** y `MainViewModel` reducido a la consola.
4. **Navegación por `IModuleNavigator`**; desaparece `new XWindow()` de los ViewModels.
5. **Cierre de módulo ≠ detener emisión**: solo la consola manda en el apagado, con confirmación si hay aire activo.
6. **Estética Quasar conservada** (oscuro + acentos neón) con la paleta canónica reducida de 62 a ~14 valores.

## 10. Cómo se mide que mejoró

| Métrica | Antes (UI-0) | Tras UI-1/2 | Tras UI-3a | Objetivo |
|---|---|---|---|---|
| Líneas de `MainWindow.xaml` | 1.680 | 1.527 | 1.583 (UI-3a dejó 1.534; UI-3b-1 suma la franja de módulos y el estilo de botón) | ≤ 350 (UI-3b paso 2) |
| Literales hex en vistas | 328 | **0 en todos los `.xaml` fuera de `Themes/`** | 0 | 0 (solo en `Tokens.xaml`) ✔ |
| Literales `FontFamily` en vistas | 14 | **0** (todos vía `Sys.Font.Clock`) | 0 | 0 ✔ |
| Estilos/plantillas duplicados | ~45 en línea + 8 locales | 36 en línea + 1 local (`LibraryManagementWindow`) | igual | 100% en `Controls.xaml` (UI-3b/UI-4) |
| Pestañas de la suite | 8 dentro de la consola | 8 | 8, pero **4 dominios ya tienen ventana propia** (Programación, DSP+PFL, Streaming+Grabación, Reportes) | 0 (6 ventanas + 3 diálogos) |
| Enlaces efectivos del tema | 7 en toda la app | 234 `DynamicResource` solo en la consola; 4 ventanas sin un solo hex | 234 | todas las superficies ✔ |
| Propiedades de reenvío en `MainViewModel` | 69 contadas | 66 | 66 | 0 (UI-3b) |
| Contraste del texto más usado | 3,4:1 (footer, calculado sobre `#0F111A`) | **5,4:1** | 5,4:1 | ≥ 4,5:1 ✔; críticos ≥ 7:1 |
| Atajos F1–F12 válidos con | solo la consola (12 `KeyBinding` copiados) | **cualquier ventana** (`StudioKeys.CartWall`, 1 declaración) ✔ | idem | cualquier ventana |
| Ventanas de módulo abiertas a la vez | 1 (modal: bloquea la consola) | N no modales, una por módulo ✔ | 6 registradas en `ModuleNavigator` y verificadas abiertas (Biblioteca y DSP siguen sin poderse probar sin tus credenciales) | idem |
| **Acceso a un módulo sin sesión** | botón apagado o pestaña vacía | LIBRERÍA pide acceso | **la puerta está en `Open()`**: DSP/Usuarios/Biblioteca no construyen siquiera la ventana sin administrador, y el resto abre y deja ver el estado | idem, re-evaluado al abrir |
| **Funciones muertas recuperadas** | — | — | GRABACIÓN: `RecordingFiles` se rellena por fin y ELIMINAR recibe el archivo (antes no tenía `CommandParameter`) | 0 botones que no hagan nada |
| Estado de un módulo al reabrir | perdido | perdido (la ventana se destruye al cerrar) | perdido | conservado (ocultar, no destruir) → UI-3b |
| **VU de salida master visible** | **no** (recortado por arriba, D1) | no | **sí**: etiqueta en `1016,210` y dos barras `180x20`, también maximizada | siempre |
| **Consola dentro del área útil del panel 1600 × 900** | **no**: `80,-20 1440x900`, barra de título fuera (D2) | no | **sí**: `80,0 1440x860`, los 4 bordes dentro (`WindowFitting`) | idem, y por monitor en UI-6 |
| **Etiquetas recortadas** | sí: "AUTO PLAYLI", "LIMPIAR HIS", "Duració" y el pie pegado (D3) | sí | **no**: `WrapPanel` en las dos pestañas, `TrimmingCell` con tooltip en las tablas, pie en `Grid` | 0 |
