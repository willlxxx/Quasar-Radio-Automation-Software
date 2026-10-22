# Línea base de la interfaz (UI-0)

Capturas tomadas el 2026-10-22 sobre la compilación de `bin/Debug/net10.0-windows`, lanzando `quasar_radio.exe` desde esa carpeta (es donde vive la librería real) y cerrando la aplicación por `CloseMainWindow()`. No se reprodujo nada ni se activó el stream: el arranque no arranca playout ni Icecast por sí solo.

| Captura | Ventana |
|---|---|
| [`consola_antes_1440.png`](ui-baseline/consola_antes_1440.png) | 1440 × 900, como está declarada en `MainWindow.xaml:5` |
| [`consola_antes_max.png`](ui-baseline/consola_antes_max.png) | maximizada en la pantalla primaria, que es **1600 × 900** |

Estas dos imágenes son la referencia visual contra la que se compara todo el plan de interfaz. Antes de tocar un color, la consola se ve así y funciona así.

## 1. Defectos visibles que la captura demuestra

**D1 · El VU de salida master no se ve.** `MainWindow.xaml:988-1005` mete el medidor en un `StackPanel` con `VerticalAlignment="Bottom"` dentro de una fila `*`. El contenido es más alto que la fila, así que el desbordamiento **recorta por arriba**: en las dos capturas aparecen "DETECTOR SILENCIO" y "VOLUMEN MASTER", pero nunca "NIVEL DE SALIDA MASTER (VU METER)" ni los dos `LedVuMeterControl`. El operador está emitiendo sin ver su nivel de salida. Es el defecto más grave de los cuatro y es puramente de layout.

**D2 · La consola no cabe en la pantalla del fundador.** `Height="900"` con barra de título y taskbar en un panel de 1600 × 900 deja el pie de ventana cortado: en `consola_antes_1440.png` se ven los bordes de la fila de botones asomando bajo la taskbar. La pantalla base real es **1600 × 860 útil**, no 1440 × 900.

**D3 · Etiquetas truncadas.** En la columna derecha: "AUTO PLAYLI", "SMART PLA", "LIMPIAR HIS" (maximizada) y "AUTO PLAYL", "SMART PLA", "LIMPIAR HIS" (1440). Los botones no se ajustan a su contenedor.

**D4 · El logo de la emisora es ilegible.** El bloque "QUASAR RADIO" del header se renderiza como una caja clara con texto claro. Contraste insuficiente sobre `#FFFFFF`/casi-blanco.

**D5 · Las 8 pestañas no caben en una fila.** Se parten en dos hileras ("TANDAS / STREAMING / GRABACIÓN / ESTADÍSTICAS" arriba, "BASE DE DATOS / PROCESADOR DSP / CARTWALL (SFX) / AUX PFL" abajo), y la segunda hilera queda visualmente por encima del contenido, así que el orden de lectura no es el orden de las pestañas.

**D6 · Solape en la columna master.** El texto "100%" del volumen cae encima del deslizador de la fila de botones MIC/CROSSFADE/SILENCIO.

**D7 · Los clips de hora siguen siendo los generados.** La pestaña BASE DE DATOS lista `HRS00_O` … `HRS14_O` (falta `HRS06_O`) todos con **00:02** y "Artista Desconocido". Son los tonos que se escribieron encima de las grabaciones reales: hasta que el fundador restaure su respaldo, la locución de hora está reproduciendo material de prueba. `CustomTimeEngine` busca `HRS{hh}_O` primero, así que los encuentra a ellos antes que a ningún otro.

## 2. Inventario de funciones por pestaña

Nada de esto se puede perder al repartir la interfaz en ventanas. Miembros enlazados, extraídos por rango de líneas de `MainWindow.xaml`:

| Pestaña | Líneas | Miembros que consume |
|---|---|---|
| BASE DE DATOS | 1161–1203 | `DatabaseService.Categories`, `.Genres`, `.SearchText`, `.FilteredTracks` · `AddTrackFromDbToPlaylistCommand` · `GenerateAutoPlaylistCommand` · `GenerateSmartPlaylistCommand` · `ClearRotationHistoryCommand` · `PlayAux1Command` |
| PROCESADOR DSP (`IsEnabled="{Binding IsAdmin}"`) | 1204–1258 | `DspProcessor.IsAgcEnabled`, `.AgcTargetDb`, `Band31Hz`…`Band16kHz` (10), `.CompThreshold`, `.CompRatio`, `.CompMakeup`, `.EqPresets`, `.CompressorPresets` |
| CARTWALL (SFX) | 1259–1334 | `CartPads`, `TriggerCartPadCommand`, `AssignCartPadCommand`, `ClearCartPadCommand`, `StopAllCartPadsCommand`, `CartwallMasterVolume`, `CartwallVolumePercentageText`, `ShortcutText` |
| AUX PFL (`IsEnabled="{Binding CanOperate}"`) | 1335–1380 | `AuxPlayers.Aux1`/`Aux2` → `.CurrentTrack.Title`, `.CurrentTrack.Artist`, `.Volume`, `.Stop` |
| TANDAS | 1381–1430 | `CommercialBlocks` → `Name`, `ScheduleText`, `Spots.Count`, `TotalDurationFormatted` · `AddSpotToBlockCommand`, `TriggerCommercialBlockNowCommand` |
| STREAMING | 1431–1526 | `StreamingService.ServerUrl`, `.ServerPort`, `.MountPoint`, `.Password`, `.FFmpegPath`, `.StreamingStatusText`, `.StatusMessage`, `.ConnectionTimeFormatted`, `.BytesSentFormatted`, `.StreamDurationFormatted`, `.Bitrate`, `.CircuitBreakerState`, `.BufferUnderrunCount`, `.BufferOverrunCount` · `ToggleStreamingCommand`, `TestIcecastConnectionCommand` · `CurrentNowPlayingText` |
| GRABACIÓN | 1527–1596 | `RecordingService.RecordingStatusText`, `.CurrentFileName`, `.RecordingFolder`, `.RecordingStartTimeFormatted`, `.RecordingDurationFormatted`, `.BytesRecordedFormatted` · `ToggleRecordingCommand`, `OpenRecordingFolderCommand`, `DeleteRecordingCommand` |
| ESTADÍSTICAS | 1597–1666 | `StatisticsService.SessionTrackCount`, `.TotalSessionPlayTime`, `.TotalUniqueTracks`, `.TotalPlaybackRecords` · `LoadStatisticsCommand`, `SaveStatisticsCommand`, `ClearStatisticsCommand`, `GenerateStatisticsReportCommand` |

Más lo que ya no está en pestañas: header (186–326), Deck A (335–644), Deck B (645–954), master (955–1028), rejilla de emisión con arrastre (1036–1133), pie con `USUARIOS` / `CERRAR SESIÓN` / estado de carga (1667+), y `F1`–`F12` → `CartWallViewModel.TriggerCartPadCommand` (9–22).

## 3. Comprobación de tema

En las dos capturas el selector muestra "Studio Dark Neon". Los otros tres temas solo cambian el fondo liso de la ventana principal: `ThemeViewModel.ApplyTheme` fija `WindowBackground`, `CardBackground` y `AccentColor`, y esas tres propiedades están enlazadas 7 veces en toda la aplicación (4 · 1 · 2), ninguna de ellas en las otras cuatro ventanas. No se tomaron capturas de los 4 temas porque la diferencia demostrable es un solo rectángulo de fondo; el diagnóstico I1 del plan queda confirmado en la práctica.

## 4. Después de UI-1 · sistema de diseño gobernanado

Compilación limpia (`0 Advertencia(s) / 0 Errores`) y capturas tomadas el 2026-10-22 a las 12:18–12:36 con el mismo procedimiento: escribir `%APPDATA%\QuasarRadio\appearance.json`, lanzar `quasar_radio.exe` desde `bin/Debug/net10.0-windows`, capturar por `GetWindowRect` + `CopyFromScreen`, y cerrar por UI Automation (`WindowPattern.Close`) con `Kill()` de respaldo. Cada ejecución terminó con `RESTANTES=0`: no se dejó ninguna instancia abierta en la máquina del fundador.

| Captura | Qué demuestra |
|---|---|
| [`consola_tema_neon.png`](ui-baseline/consola_tema_neon.png) | consola con **Studio Dark Neon** (la referencia visual sigue siendo la de antes) |
| [`consola_tema_cyan.png`](ui-baseline/consola_tema_cyan.png) | la **misma** consola con **Cyan Broadcast**: header, decks, botones, pestañas, tablas y pie repintados; Deck A/B conservan su identidad cian/morada |
| [`consola_tema_charcoal.png`](ui-baseline/consola_tema_charcoal.png) · [`consola_tema_navy.png`](ui-baseline/consola_tema_navy.png) | Charcoal Pro y Deep Navy FM; los dos últimos se distinguen por el matiz del header y del panel de pestañas |
| [`login_tema_cyan.png`](ui-baseline/login_tema_cyan.png) · [`login_tema_neon.png`](ui-baseline/login_tema_neon.png) | **segunda ventana** (`LoginWindow`, 430 × 430) bajo dos temas: prueba que el mecanismo no es exclusivo de la consola |
| [`combobox_despues.png`](ui-baseline/combobox_despues.png) | "Todas"/"Todos" legibles: el `ControlTemplate` propio del `ComboBox` sustituye al chrome claro de Aero |

Métricas reales tras UI-1: **0** literales hex en *todos* los `.xaml` fuera de `Themes/` (no solo en las 5 vistas del plan), 100 claves en `Themes/Tokens.xaml`, 4 archivos de tema, `MainWindow.xaml` en 1.527 líneas (bajó de 1.680 al irse `Window.Resources`; la reducción a ≤350 llega con UI-3).

### Deltas visuales deliberados (no son regresiones)

1. **`Brush.Text.Faint` subió de `#55657B` a `#7A8BA3`.** Calculado sobre `Brush.Surface.Base` `#0F111A`: **3,4 : 1 → 5,4 : 1**, cruza el umbral AA de 4,5 : 1. Afecta al pie de ventana y a las etiquetas secundarias; el texto se ve más claro que antes a propósito.
2. **Los estilos implícitos de `Button` y `ComboBox` cambian el chrome de todo control que no tuviera estilo.** Antes, un `<Button>` sin `Style` en un diálogo salía con el botón gris claro de Aero; ahora sale con `Brush.Surface.Raised` y radio del sistema. Esto es lo que arregló de paso el defecto **D4** (el logo ilegible del header era chrome de Aero pintado por encima).

### Hallazgos nuevos que condicionan UI-2

**H1 · La consola arranca sin sesión y deja medio panel apagado.** `%LocalAppData%\QuasarRadio\users.json` **sí existe** y tiene dos cuentas (`admin`, rol Admin; `willlxxx`, rol Operator). Lo que no hay es sesión activa: `App.OnStartup` abre la consola "sin login obligatorio", así que `App.Auth.IsAuthorized(Admin)` es `False` desde el minuto cero y con ello el `ComboBox` de tema (`MainWindow.xaml:144`, `IsEnabled="{Binding IsAdmin}"`), la pestaña PROCESADOR DSP y el botón 🎵 LIBRERÍA (CanExecute del comando). UI Automation lo confirmó: `Expand` sobre el combo responde *«No se permite la operación en un elemento que no se puede habilitar»* y el botón figura `habilitado=False`. Consecuencia para este plan: el repintado *en vivo* al cambiar de tema no se ha podido ejercitar sin credenciales, y ninguna ventana de módulo se puede abrir todavía. Resuelto en parte en UI-2b (el botón LIBRERÍA ahora pide acceso en vez de estar apagado) y pendiente de verificación con él delante.

**H2 · Las ventanas de módulo hoy se abren modales.** `MainWindow.xaml.cs:30-46` (`OnManageUsersClick`) usa `ShowDialog()` para `LoginWindow` y para `UserManagementWindow`. Con el hilo UI metido en el bucle modal, **UI Automation deja de ver la ventana nueva** por `RootElement.FindAll`: solo aparece al enumerar con `EnumWindows` de Win32. Cualquier verificación automatizada de la consola multi-ventana tiene que usar Win32, y el plan de UI-2 debe decidir si las ventanas de módulo siguen siendo modales (no) o independientes (sí).

**H3 · La consola está posicionada fuera de la pantalla.** La ventana mide 1440 × 900 pero su rectángulo real es `80,-20`: la barra de título cae 20 px por encima del borde superior. Refuerza D2 y hay que resolverlo en UI-3 con geometría guardada en `layout.json`, no con `Height="900"` fijo.

**H4 · La aplicación cambia de librería según el directorio de trabajo.** Lanzar `quasar_radio.exe` con CWD en la raíz del proyecto (lo que hace `Start-Process` si no se le pasa `-WorkingDirectory`) carga los 5 fixtures de prueba de `musica/` (`pista_01` … `pista_05`, `jingle_quasar_01`, `sep_giro_01`, `spot_comercial_01`, `voz_locucion_01`) y muestra "Cargadas 5 canciones"; lanzado desde `bin/Debug/net10.0-windows` carga la librería real (14 canciones, `HRS00_O`…). Captura del error: [`consola_cwd_equivocado.png`](ui-baseline/consola_cwd_equivocado.png). El run equivocado además **escribió `nowplaying.json` en la raíz del repo** (está en `.gitignore:35`, y su contenido es la cabecera genérica `Quasar Radio Pro - En Vivo`, no metadatos reales); lo moví a `Temp/qsr_probe/nowplaying_raiz_20261022_1241.json` y no quedó ningún otro fichero suelto. Comprobado con `git status`: ningún dato suelto sin trackear, solo los ficheros nuevos del plan (`ThemeService.cs`, `ModuleNavigator.cs`, `StudioKeys.cs`, `Themes/`, `docs/`). Esto es exactamente lo que ataca la Fase 2.5 del plan de arquitectura (`DataPaths`), y obliga a que todo script de verificación de UI fije `-WorkingDirectory` al `bin`.

## 5. Después de UI-2 · navegación, permisos y teclas

Cambios entregados (compilación en 0 errores / 0 advertencias):

- **`ModuleNavigator.cs`** — `IModuleNavigator` con `Open` / `IsOpen` / `Toggle` / `EnsureAdmin` y el evento `ModuleClosed`. Una ventana por módulo: si ya está abierta se reactiva en vez de crear otra. Las ventanas de módulo pasan a **transient** en DI (WPF no permite re-mostrar una `Window` cerrada) y se abren **no modales** con `Owner` = consola.
- **Geometría recordada** en `%APPDATA%\QuasarRadio\layout.json` con escritura atómica (`.tmp` + `File.Replace`, el patrón de `ThemeService`), debounce de 500 ms y descarte de posiciones fuera de cualquier pantalla (`WindowGeometry.IsSane()`), para que un monitor desconectado no deje una ventana inalcansable.
- **`StudioKeys.cs`** — F1–F12 → pad 1–12 declarados una sola vez como propiedad adjunta `local:StudioKeys.CartWall="True"`; ya no son 12 `KeyBinding` copiados en la consola. El comando se resuelve en DI en el momento de pulsar, así que funciona con el foco en la ventana que esté delante. Aplicado en consola, Biblioteca y Usuarios; el `TrackEditorDialog` se queda sin botonera a propósito (es un editor modal).
- **Cierre protegido** — `MainWindow.OnClosing` pregunta si hay playout, micro, grabación o stream activos (`MainViewModel.IsAirActive()`); cerrar un módulo no cierra la aplicación.
- **Permisos** — `OpenLibraryManagementCommand` deja de estar apagado: siempre es ejecutable y `EnsureAdmin()` pide el acceso al abrir. `USUARIOS` pasa por el mismo camino. La recarga de la base de datos, que antes colgaba del cierre modal, ahora va en `ModuleClosed`.

Verificación en vivo (lanzando desde `bin/Debug/net10.0-windows`, con `-WorkingDirectory` fijado tras H4):

| Comprobación | Resultado |
|---|---|
| Pulsar 🎵 LIBRERÍA sin sesión | abría el `LoginWindow` (antes: botón apagado, sin respuesta) — capturado en [`login_tema_neon.png`](ui-baseline/login_tema_neon.png) |
| Cerrar la consola sin aire activo | `SALIO_SIN_MATARLO=True`: el proceso termina solo, el `Closing` nuevo no lo bloquea |
| Consola antes / después de UI-2 | 0,42 % de píxeles distintos sobre 1440 × 900. Las diferencias son el reloj, la taskbar que asoma detrás y **el botón LIBRERÍA**, que pasa de atenuado a activo ([`diff_ui1_ui2.png`](ui-baseline/diff_ui1_ui2.png)) |
| Procesos huérfanos tras cada ejecución | `RESTANTES=0` en las 6 ejecuciones |

**Pendiente de su contraseña:** abrir Biblioteca de verdad (ventana no modal, geometría guardada, recarga al cerrar) y probar F1–F12 con foco en otra ventana. No intento acceder a `users.json` ni adivinar credenciales. También queda comprobar que el `LibraryManagementWindow` arranca con la propiedad adjunta nueva: el `build` valida el BAML pero no el parse en caliente de una ventana que no puedo abrir sin sesión.
