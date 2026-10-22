# Plan de mejora de arquitectura — Quasar Radio

Fecha: 2026-10-22 · Estado del código auditado: build limpio (0 errores, 0 advertencias) en .NET 10.0.401, `net10.0-windows`, WPF + NAudio 3.0.1.

Este documento no cambia código. Describe qué está mal, por qué importa para una emisora en vivo, y en qué orden conviene corregirlo.

---

## 1. Diagnóstico con evidencia

| # | Hallazgo | Evidencia | Riesgo real |
|---|---|---|---|
| A1 | **Cero `async`/`await` en todo el proyecto** | `grep await` → 0 coincidencias en `.cs` raíz y `ViewModels/`. Peor caso concreto: `ViewModels/LibraryViewModel.cs:66` llama `_databaseService.LoadLibrary()` **desde el constructor**, y ese escaneo de carpetas es síncrono (`AudioDatabaseService.cs:172-205`) | Todo I/O (escaneo de librería, ffmpeg, HTTP de Icecast) corre síncrono o en `Task.Run` fire-and-forget. Un disco lento o un servidor caído congela la interfaz **mientras estás al aire** |
| A2 | **Reintentos con hilos bloqueados en streaming** | `Thread.Sleep` en `IcecastStreamingService.cs:490,531,601`; `pendingRead.Wait(100)` + `.Result` en `IcecastStreamingService.cs:707-709` (espera acotada a 100 ms, pero es async-over-sync en el worker); `CustomTimeEngine.cs:96` | Reconexión del stream con esperas bloqueantes y mezcla de modelos: difícil de cancelar y de auditar |
| A3 | **Los motores de audio dependen de WPF** | `Application.Current.Dispatcher` en `CartwallEngine.cs:93`, `IcecastStreamingService.cs:340,439,462,723,733`, `MainViewModel.cs:472,483,509,612,615`, `LibraryManagementViewModel.cs:542,551`, `ViewModels/AudioPlayerViewModel.cs:313,337,349`, `ViewModels/LibraryViewModel.cs`; además `LibraryViewModel.cs:60` lee el estático `App.Auth` | Nada del núcleo es ejecutable sin la app abierta → imposible de automatizar o testear |
| A4 | **Dos `Dispatcher.Invoke` síncronos que cruzan de hilo real** | `CartwallEngine.cs:91-104` (`PlaybackStopped` de NAudio → `Dispatcher.Invoke`) y `IcecastStreamingService.cs:340` (worker de streaming → `Dispatcher.Invoke` en un `finally`) | Son los únicos bloqueos de hilo-a-hilo verdaderos: si el hilo UI está ocupado, el callback de audio o el cierre del stream quedan espera-que-te-veo. Los otros 10 `Invoke` síncronos (`MainViewModel.cs:472,483,509`, `AudioPlayerViewModel.cs:313,337,349`, etc.) **no** cruzan hilo: se emiten desde `DispatcherTimer`, ya están en el hilo UI y el `Invoke` es marshaling redundado |
| A5 | **Cadena de audio duplicada 5 veces** | `AudioEngine.cs:288-305`, `AudioEngine.cs:336-351`, `CartwallEngine.cs:76-87`, `MicEngine.cs:56-58`, más variante sin DSP en `AuxPlayerEngine.cs:56-62` | Cada arreglo de DSP, formato o volumen hay que aplicarlo en 5 sitios. Ya hay divergencia: los decks del preload no usan `LevelMeterSampleProvider` |
| A6 | **Fachada muerta: `MainViewModel` reenvía 42 comandos** | `MainViewModel.cs` (727 líneas) con propiedades y comandos tipo `public ICommand PlayCommand => AudioPlayerViewModel.PlayCommand;` (líneas 152-185) | Dos capas de VM donde una basta. Cada binding nuevo exige tocar dos archivos; fuente de bugs silenciosos de binding |
| A7 | **`MainWindow.xaml` monolítico y sin reutilización de estilos** | 128 KB, **1.680 líneas**, 222 bindings, 53 `Style`/`DataTemplate`/`ControlTemplate`. Solo 8 están en `<Window.Resources>` (líneas 24-176); **los otros ~45 están inline en el árbol visual** | Editar una sección obliga a recompilar y revalidar toda la consola; los estilos inline no los puede aprovechar ninguna otra ventana, y el tema oscuro se copia y pega |
| A8 | **`IAudioEngine` declarada y nunca usada** | `grep IAudioEngine` → solo la definición en `IAudioEngine.cs:6` | La abstracción existe pero el DI registra el tipo concreto (`App.xaml.cs:30`), así que no aporta desacoplamiento ni testabilidad |
| A9 | **Mezcla de DI con service-locator y `new`** | `LibraryManagementViewModel.cs:202`: `databaseService ?? App.Services?.GetService<AudioDatabaseService>() ?? new AudioDatabaseService()` | Tres rutas de construcción para el mismo servicio; un `new AudioDatabaseService()` propio duplica el estado de la librería |
| A10 | **Servicio de datos hereda de `ViewModelBase`** | `AudioDatabaseService.cs:9` | Un repositorio con `INotifyPropertyChanged` de presentación: capa de datos contaminada |
| A11 | **Persistencia JSON no atómica ni bloqueada** | `TrackMetadataService.cs:95,255,278`, `StatisticsService.cs:387,552`, `NowPlayingService.cs:42,46` usan `File.WriteAllText` directo. `AuthService.cs:187` sí escribe en temporal y renombra | Corte de luz o crasheando a mitad de escritura deja `track_metadata.json` truncado → pierdes cue points, BPM y ganancias de toda la librería |
| A12 | **Rutas relativas al directorio de trabajo** | `AudioDatabaseService.cs:177-187` (`"musica"`, `"jingles"`, `"comerciales"`…), `StatisticsService.cs:78-79`, `TrackMetadataService.cs:48-49`, `NowPlayingService.cs:20-21` | Si el operador abre el `.exe` desde un acceso directo con otra "Carpeta de inicio", la app no encuentra la música y recrea carpetas vacías en otro disco |
| A13 | **Silencio de errores** | 14 `catch` vacíos + 51 `catch (Exception …)` repartidos (concentración: `LibraryManagementViewModel.cs` 12, `IcecastStreamingService.cs` 9, `RecordingService.cs` 7, `TrackMetadataService.cs` 6) | Un fallo de grabación o de metadata puede pasar sin dejar rastro en `logs/` |
| A14 | **Sin red de seguridad** | `git status` → *not a git repository*; no hay `.sln`, ni proyecto de pruebas, ni `appsettings.json` | Sin historial no hay rollback; sin pruebas cualquier refactor del motor de audio es a ciegas |
| A15 | **Dependencias declaradas y sin usar** | `Serilog.AspNetCore` y `Serilog.Settings.Configuration` en `quasar_radio.csproj:15-16`: verificado con grep, no hay `UseSerilog`, `ReadFrom.Configuration`, `IHostBuilder` ni `appsettings.json` en el proyecto. Además `node_modules/` con `@github/copilot` (`package.json`) dentro de un proyecto .NET | Ruido de restore y confusión sobre la configuración real: hoy los niveles de log están hardcodeados en `App.xaml.cs:80-89` |
| A16 | **Contenido de prueba vacío** | `musica/`, `horarias/`, `comerciales/`, `jingles/`, `separadores/`, `voces/`, `grabaciones/` con 0 archivos | No se puede verificar una mejora de emisión sin material; solo hay un `musica.mp3` suelto en la raíz |
| A17 | **El reloj de emisión vive en el hilo de la interfaz** | `AudioEngine.cs:152-162` crea `_playbackTimer` y `_fadeTimer` como `DispatcherTimer` (100 ms y 50 ms); `OnPlaybackTimerTick` (`AudioEngine.cs:394-446`) es lo único que dispara cue-out, crossfade, fin de pista y niveles de VU | **El problema estructural más serio**: si el hilo UI se ocupa (un `LoadLibrary`, un diálogo modal, un repaint pesado del XAML de 1.680 líneas), la detección de fin de pista se retrasa. La pista en curso no se silencia porque `WaveOut` reproduce en su propio hilo, pero sí puede haber solapamiento entre decks o silencio al final de la pista. Además el motor no puede funcionar sin ventana |
| A18 | **El contenido de emisión vive en `bin/`, no en el proyecto** | Verificado el 2026-10-22: las carpetas fuente `musica/`, `jingles/`, `separadores/`, `voces/` y `comerciales/` están vacías, mientras `bin/Debug/net10.0-windows/musica/` tiene 55 archivos reales y `.../horarias/` 110 clips de locución fechados 2026-09-23. Causa: `LoadLibrary()` resuelve rutas relativas al CWD y la app se lanza desde la carpeta de salida; solo `horarias/**` está declarado `Content` en el `.csproj` | **Riesgo de pérdida de datos, no solo arquitectónico**: `bin/` está en `.gitignore`, así que la biblioteca entera es invisible para git y un `dotnet clean` --o un archivo nuevo con nombre colisionante que el build copie encima-- la destruye sin dejar rastro. Ocurrió el 2026-10-22: ver `docs/INCIDENTE-2026-10-22-horarias.md`. La Fase 2.2 debe convertir esto en un `DataPaths` con la librería fuera de `bin/` |

---

## 2. Arquitectura objetivo

Cuatro capas con una regla única: **solo la capa de presentación conoce WPF**.

```
quasar_radio.Core          (classlib, sin System.Windows)
  Audio/        IAudioEngine, IAudioPlayer (pipeline), DspProcessor, AudioFormatFactory
  Scheduling/   CommercialScheduler, RotationEngine, SilenceDetector, TimeEngine
  Streaming/    IIcecastClient, CircuitBreaker, NowPlayingWriter
  Persistence/  ITrackRepository, IStatisticsStore, IUserStore  (escritura atómica)
  Ports/        IUiDispatcher (para no nombrar el Dispatcher), IClock, IFileSystem

quasar_radio.App           (WPF, el proyecto actual)
  Views/        ConsoleWindow + UserControl por módulo (Transport, CartWall, Decks,
                Playlist, Streaming, Stats, Library)
  ViewModels/   uno por módulo, sin fachada de reenvío
  CompositionRoot.cs        DI explícito; nada de App.Services ni `new` fuera de aquí

quasar_radio.Tests         (xUnit)
```

Pipeline de audio en un solo punto, reutilizado por decks, cartwall, micro y aux:

```
IAudioSource (factory: ogg→Vorbis, flac/aac/m4a→MediaFoundation, resto→AudioFileReader)
   → BroadcastSampleProvider (ECU + AGC + compresor + limitador + espectro)
   → LevelMeterSampleProvider
   → VolumeSampleProvider
   → WaveOut
```

---

## 3. Hoja de ruta por fases

Ordenado por riesgo de antena, no por elegancia. Cada fase termina con `dotnet build` en 0/0 y una prueba manual de emisión.

### Fase 0 — Red de seguridad (antes de tocar nada) · ~1 sesión

| Tarea | Detalle | Estado 2026-10-22 |
|---|---|---|
| 0.1 Git | `git init -b main`, `.gitignore` ampliado: además de `bin/ obj/ logs/`, se excluyen `node_modules/`, los `.exe` de ffmpeg (634 MB, por encima del límite de GitHub), `graphify-out/` y la salida de ejecución (`nowplaying.*`, `statistics.json`, `grabaciones/`, `reporte_emision_*.txt`) | hecho, **commit pendiente de tu identidad escrita en tu mensaje** |
| 0.2 Solution | `quasar_radio.sln` (formato clásico, no `.slnx`) con `quasar_radio.csproj` añadido. El SDK 10.0.401 genera `.slnx` por defecto; se forzó `.sln` por compatibilidad con VS/VS Code | hecho, build 0 errores / 0 advertencias |
| 0.3 Material de prueba | Clips generados con `ffmpeg/bin/ffmpeg.exe` en `musica/` (5 formatos), `jingles/`, `separadores/`, `comerciales/` y `voces/`. **Regla nueva por el incidente:** nunca usar nombres que la app busca (`HRS*`, `MIN*`) ni escribir en carpetas que el `.csproj` copia a la salida sin comprobar si ya existen | hecho, **con daño colateral en `horarias/`** -- ver `docs/INCIDENTE-2026-10-22-horarias.md` |
| 0.4 Rutina de humo | `docs/RUTINA_HUMO.md`: 8 pasos + duraciones esperadas + cómo lanzar la app desde la carpeta de salida para que vea tu librería real | escrita; falta que la ejecutes (pasos 3-8 requieren la GUI) |

**Criterio de salida:** `git log` con la base marcada; la rutina de humo ejecutada sin incidentes.
**Estado del criterio: NO cumplido.** Queda pendiente el commit de línea base (sin identidad no se intenta) y los pasos
3-8 de la rutina. Además se destapó A18: el contenido real está en `bin/` y no tiene respaldo.

### Fase 1 — Que no se congele al aire (máximo impacto, riesgo bajo) · 2-3 sesiones

- **1.1** Convertir a `async/await` el I/O de usuario: carga de librería (`AudioDatabaseService.LoadLibrary`), importación de archivos, `TrackMetadataService` y `StatisticsService.Save`. Ningún `File.ReadAllText`/escaneo de carpeta en el hilo UI.
- **1.2** Sustituir `Thread.Sleep` de reconexión en `IcecastStreamingService.cs:490,531,601` por `Task.Delay` con `CancellationToken`, y reemplazar el híbrido `pendingRead.Wait(100)` + `.Result` (`IcecastStreamingService.cs:707-709`) por lectura asíncrona con `await reader.WaitToReadAsync(ct)`.
- **1.3** Aislar los dos `Dispatcher.Invoke` que sí cruzan de hilo: `CartwallEngine.cs:93` (callback de NAudio) e `IcecastStreamingService.cs:340` (worker en el `finally`) → `BeginInvoke`/`InvokeAsync`. Los otros 10 `Invoke` síncronos son redundantes porque ya corren en el hilo UI por el `DispatcherTimer`: eliminar el marshaling en cuanto el reloj salga del hilo UI (Fase 3.6), no antes.
- **1.4** Quitar `LoadLibrary()` del constructor de `LibraryViewModel.cs:66` y hacerlo asíncrono con indicador de carga; hoy el arranque de la app escanea 6 carpetas de audio en el hilo de UI.
- **1.5** Serializar las escrituras de estado: cada servicio de persistencia con su `SemaphoreSlim(1,1)` y `CancellationTokenSource` para descartar escrituras obsoletas.

**Criterio de salida:** con el disco externo desconectado y el servidor Icecast caído, la consola sigue respondiendo y el crossfade no se corta.

### Fase 2 — Datos a prueba de cortes · 1-2 sesiones

- **2.1** Extraer un `AtomicJsonFileWriter` (escribir `.tmp` → `Flush` → `File.Replace`) basado en el patrón que ya usa `AuthService.cs:187`, y aplicarlo en `TrackMetadataService` (3 sitios), `StatisticsService` (2) y `NowPlayingService` (2).
- **2.2** Resolver **todas** las rutas desde un `DataPaths` único basado en `AppContext.BaseDirectory` (o `%APPDATA%\QuasarRadio`), eliminando las rutas relativas actuales. Incluir `horarias/` y `musica/`.
- **2.3** Rotación de respaldo: `track_metadata.json.bak` con las últimas N versiones; hoy solo hay un `_backup` plano.
- **2.4** Decisión pendiente: ¿se queda en JSON o se migra a SQLite? Ver §4.

**Criterio de salida:** matar el proceso a mitad de guardado y reabrir; la librería conserva cue points y BPM.

### Fase 3 — Núcleo desacoplado y auditable · 2-3 sesiones

- **3.1** Crear `quasar_radio.Core` sin `System.Windows`; mover motores, planificación, streaming y persistencia. Introducir el puerto `IUiDispatcher` para que el núcleo no nombre `Application.Current`.
- **3.2** **Unificar la cadena de audio** (A5) en un `AudioGraphFactory`: una sola implementación usada por decks, preload, cartwall, micro y aux, con bandera opcional de DSP/medición. Corrige de paso la divergencia del preload (sin `LevelMeterSampleProvider`).
- **3.3** Hacer efectivo `IAudioEngine`: registrar la interfaz en DI y que los ViewModels dependan de `IAudioEngine` (A8). Igual para `ICartwallEngine`, `IStreamingService`, `IAudioDatabase`.
- **3.4** Quitar el service-locator y los `new` fuera del composition root (A9), y cambiar `AudioDatabaseService : ViewModelBase` para que no herede de presentación (A10).
- **3.5** Sustituir los 14 `catch` vacíos por registro con contexto (`Log.Error(ex, ...)`) o por propagación tipada; revisar los 51 `catch (Exception)` para que no oculten `OperationCanceledException`.
- **3.6** **Sacar el reloj de emisión del hilo UI** (A17): sustituir los `DispatcherTimer` de `AudioEngine.cs:152-162` por un temporizador propio (`PeriodicTimer` en un worker, o el callback de posición de `WasapiOut`/`WaveOutEvent`) que notifique al ViewModel a través de `IUiDispatcher`. Es la tarea de mayor valor del plan y la que más cuidado pide: se hace **después** de unificar la cadena en 3.2, con la rutina de humo de la Fase 0 ejecutada antes y después.

**Criterio de salida:** un test xUnit reproduce un deck en modo consola (sin ventana) y comprueba cue in/out y crossfade.

### Fase 4 — Capa de presentación sostenible · 3-4 sesiones

- **4.1** Deshacer la fachada (A6): `MainWindow.xaml` toma el `DataContext` por sección (un `DataContext` distinto por `UserControl`) y se eliminan los ~42 reenvíos de `MainViewModel`. `MainViewModel` queda solo con lo transversal (reloj, estado de emisión, navegación).
- **4.2** Partir `MainWindow.xaml` (A7) en `UserControl` + diccionarios de recursos: `TransportView`, `DeckView`, `CartWallView`, `PlaylistView`, `StreamingView`, `StatisticsView`, y `Theme.xaml` / `Controls.xaml` para los ~45 estilos que hoy están inline en el árbol visual.
- **4.3** Centralizar los atajos F1-F12 que hoy viven en `MainWindow.xaml.cs` en una tabla de comandos.
- **4.4** Migrar `LibraryManagementViewModel` (737 líneas, 12 `catch (Exception)`, `Dispatcher.Invoke` dentro del bucle de importación en las líneas 542 y 551) al mismo patrón.

**Criterio de salida:** `MainWindow.xaml` por debajo de ~20 KB; ningún comando definido dos veces en dos ViewModels.

### Fase 5 — Higiene y configuración · 1 sesión

- **5.1** Aprovechar `Serilog.Settings.Configuration` (ya instalado, hoy sin usar) moviendo la configuración de log de `App.xaml.cs:80-89` a un `appsettings.json` con niveles y rutas; si decides no hacerlo, quita `Serilog.AspNetCore` y `Serilog.Settings.Configuration` del `.csproj`.
- **5.2** Eliminar `package.json` / `package-lock.json` / `node_modules` del proyecto .NET si `@github/copilot` no cumple función.
- **5.3** Convertir magic strings (`"horarias"`, `"Pop"`, nombres de clips de `CustomTimeEngine.cs:66-74`) en constantes o configuración.
- **5.4** CI mínimo en GitHub para `dotnet build` + `dotnet test` (sin `gh` en el equipo, usar la API como ya haces en otros proyectos).

---

## 4. Decisiones

**Aprobadas por el fundador el 2026-10-22** ("aprobar todas las decisiones"), quedando como las adoptadas:

1. **SQLite para metadata y estadísticas**, manteniendo los archivos de audio en carpetas. Se abandona el JSON plano de
   `TrackMetadataService`, `StatisticsService` y `AudioDatabaseService`.
2. **WPF se queda**; no hay migración de interfaz en este plan.
3. **Sí a mover el reloj de emisión fuera del hilo UI** (Fase 3.6), ejecutándose después de unificar la cadena de audio.
4. **Rotación y planificación entran primero en pruebas unitarias** (`SmartRotationEngine`, `CommercialScheduler`).

Consecuencia nueva en el roadmap por el hallazgo A18: la **Fase 2.5** saca la biblioteca de `bin/` a una ruta de datos
estable (`%APPDATA%\QuasarRadio` o una carpeta fija elegida por ti) y añade respaldo automático antes de tocar
`AudioDatabaseService`. Sin eso, cualquier `dotnet clean` sigue siendo un riesgo de pérdida de contenido.

## 5. Fuera de alcance por ahora

Metería en otra ronda: licencia/activación, base de datos multi-estación, control por red desde navegador, y cualquier cambio visual de marca. Son funciones nuevas, no deuda arquitectónica, y mezclarlas con el refactor dispara el riesgo.

## 6. Cómo se mide que mejoró

| Métrica | Hoy | Objetivo |
|---|---|---|
| `await` en el código | 0 | I/O de usuario asíncrono al 100% |
| Referencias a `Application.Current` fuera de la capa de presentación | 6 archivos | 0 |
| Líneas de `MainViewModel.cs` | 727 | < 250 |
| `MainWindow.xaml` | 128 KB / 222 bindings | < 20 KB, dividido en UserControls |
| Construcciones de cadena de audio duplicadas | 5 | 1 fábrica |
| Escrituras no atómicas | 8 sitios | 0 |
| `catch` vacíos | 14 | 0 |
| Temporizadores de emisión dependientes del hilo UI | 2 (`_playbackTimer`, `_fadeTimer`) | 0 |
| Archivos sin dependencias muertas | 2 paquetes Serilog + `node_modules` | 0 |
| Pruebas | 0 | humo de emisión + rotación + scheduling |
| Control de versiones | sin git | git con fases comiteadas |
