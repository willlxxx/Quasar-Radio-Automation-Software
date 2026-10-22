# Rutina de humo — Quasar Radio

Se ejecuta **al cerrar cada fase** del `PLAN_MEJORA_ARQUITECTURA.md`. Lleva el control de lo que funciona hoy para
poder distinguir una regresión de un defecto preexistente.

## Cómo arrancar (importante)

Ejecuta el `.exe` **desde su propia carpeta**, no desde la raíz del proyecto:

```
cd "C:\Users\CY-KILL\Desktop\Programación y Datanálisis\quasar_radio\bin\Debug\net10.0-windows"
.\quasar_radio.exe
```

Motivo verificado: `AudioDatabaseService.LoadLibrary()` (`AudioDatabaseService.cs:172-187`) resuelve `musica/`,
`jingles/`, `separadores/`, `voces/`, `comerciales/` y `horarias/` **respecto al directorio de trabajo**. Tu biblioteca
real (55 archivos en `musica/`, 110 clips en `horarias/`) vive en esa carpeta de salida; las carpetas fuente del proyecto
están casi vacías. Si lanzas `dotnet run` desde la raíz, la librería aparecerá vacía.

## Verificación previa que ya está hecha

- `dotnet build quasar_radio.sln -c Debug` → **0 errores, 0 advertencias** (comprobado 2026-10-22).
- Los clips de prueba se leen por el mismo camino de NAudio que usa la app (reader correcto por extensión, duración de
  cabecera igual a la decodificada ±0,04 s) -- verificado con un arnés aparte que replica `AudioSourceFactory.Open`.
- **Hallazgo nuevo:** un `.aac` en contenedor ADTS se abre con `MediaFoundationReader` y reporta **39,51 s de cabecera
  para 18,04 s reales (+21 s)**. Si la emisora usa `.aac`, el CueOut y el disparo de crossfade se calculan mal.
  Archivo de caso guardado en `docs/fixtures/pista_06_aac_adts.aac` (fuera de las carpetas de librería a propósito).

## Los 8 pasos

| # | Acción | Qué debe pasar |
|---|---|---|
| 1 | Abrir la consola | `MainWindow` aparece sin pedir login (ver `App.xaml.cs:58-73`); sin excepciones en `logs/quasar_radio-<fecha>.log` |
| 2 | Cargar `musica/pista_01.mp3` a la playlist (arrastrar o botón de carga) | Entra con duración 30 s y estado `Queued`; `pista_03.ogg` debe dar 25 s y `pista_04.flac` 15 s |
| 3 | PLAY y esperar el crossfade | `pista_01` pasa al deck activo; al quedar ~4 s, `pista_02` arranca en el otro deck con fundido; ningún clip se corta antes de tiempo |
| 4 | Verificar duración leída por la app | El tiempo total en pantalla coincide con la tabla de abajo (±1 s). Si alguno difiere mucho, es el problema de cabecera del paso 2-bis |
| 5 | Pulsar F1 y F2 con un pad asignado | Suenan superpuestos al deck sin cortarlo; el pad se desasigna solo al terminar (`CartwallEngine.cs:91-104`) |
| 6 | Activar micrófono | El nivel del deck baja a ~25 % (ducking, `AudioEngine.cs:86`) y se recupera al desactivar |
| 7 | `AnnounceTimeCommand` con la hora actual | Se oye el clip de `horarias/`. **Hoy este paso fallará en parte**: ver el aviso de abajo |
| 8 | Streaming Icecast + `GenerateStatisticsReportCommand` | `nowplaying.txt` y `nowplaying.json` se actualizan; aparece `reporte_emision_<fecha_hora>.txt`; las grabaciones caen en `grabaciones/` |

Duraciones esperadas de los fixtures:

| Archivo | Formato | Reader | Duración |
|---|---|---|---|
| `musica/pista_01.mp3` | mp3 | AudioFileReader | 30 s |
| `musica/pista_02.wav` | wav | AudioFileReader | 20 s |
| `musica/pista_03.ogg` | ogg | VorbisWaveReader | 25 s |
| `musica/pista_04.flac` | flac | MediaFoundationReader | 15 s |
| `musica/pista_05.m4a` | m4a | MediaFoundationReader | 20 s |
| `jingles/jingle_quasar_01.mp3` · `_02.ogg` | mp3/ogg | | 6 s · 5 s |
| `separadores/sep_giro_01.mp3` · `_02.wav` | mp3/wav | | 4 s · 4 s |
| `comerciales/spot_comercial_01.mp3` · `_02.mp3` | mp3 | | 12 s · 8 s |
| `voces/voz_locucion_01.mp3` | mp3 | | 6 s |

Están en las carpetas fuente del proyecto. La app los verá si abres la ventana de biblioteca desde la raíz del proyecto,
o si los copias a la carpeta de salida. No se copiaron automáticamente salvo `horarias/` (que el `.csproj` sí copia).

## Aviso sobre el paso 7

31 clips de `bin/Debug/net10.0-windows/horarias/` quedaron sustituidos por tonos de prueba durante la Fase 0
(`HRS00`-`HRS23`, `HRS06_O/12_O/18_O/23_O`, `MIN15/30/45`). Hasta decidir la restauración, el anuncio de hora sonará a
tono en esos casos y a locución real en el resto (79 intactos). Detalle y opciones en
`docs/INCIDENTE-2026-10-22-horarias.md`.

## Registro de resultados

| Fecha | Build 0/0 | Pasos OK | Observaciones |
|---|---|---|---|
| 2026-10-22 | sí | 1-2 (sin ejecutar por el fundador) | Solo verificación de compilación y lectura de archivos; los pasos 3-8 requieren la GUI y aún no se han ejecutado |
