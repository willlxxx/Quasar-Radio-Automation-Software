# Incidente 2026-10-22 — 31 clips de hora sobrescritos

## Qué pasó

Durante la Fase 0 del plan de arquitectura generé tonos de prueba en la carpeta fuente `horarias/` usando los nombres
que busca `CustomTimeEngine` (`HRS00.mp3` … `HRS23.mp3`, `HRS06_O/12_O/18_O/23_O.mp3`, `MIN15/30/45.mp3`).

`quasar_radio.csproj` declara `horarias/**\*` como `Content` con `CopyToOutputDirectory=PreserveNewest`. Al ejecutar
`dotnet build`, los 31 tonos se copiaron **encima** de las grabaciones reales de locución que vivían en
`bin/Debug/net10.0-windows/horarias/`.

El error de diagnóstico fue doble:
1. Las carpetas fuente estaban vacías, así que asumí que no había contenido que dañar.
2. El contenido real de emisión vive en la **carpeta de salida**, porque `AudioDatabaseService.LoadLibrary()` usa
   rutas relativas al directorio de trabajo y la app se ejecuta desde `bin/`.

## Archivos afectados (31, en `bin/Debug/net10.0-windows/horarias/`)

`HRS00.mp3` … `HRS23.mp3` (24) · `HRS06_O.mp3`, `HRS12_O.mp3`, `HRS18_O.mp3`, `HRS23_O.mp3` (4) · `MIN15.mp3`, `MIN30.mp3`, `MIN45.mp3` (3)

Los otros 79 archivos de esa carpeta no se tocaron (conservan su fecha 2026-09-23).

## Estado inmediato

- Los 31 tonos falsos se **retiraron** de la carpeta fuente `horarias/` (quedan en `docs/incidente-2026-10-22/tonos_generados/`
  como evidencia). Un build futuro ya no volverá a copiarlos.
- Los tonos siguen dentro de `bin/Debug/net10.0-windows/horarias/`, sustituyendo la locución original.
- No hay control de versiones de esos archivos: `bin/` está en `.gitignore` y el repo se inició ese mismo día.

## Búsqueda de copias originales

Se compararon por MD5 contra los 79 archivos intactos. Ningún paquete coincide:

| Candidato | Archivos | Coincidencias con lo intacto | Cubre los 31 dañados |
|---|---|---|---|
| `F:\Programas\Radio y TV\Radio DJ\SayTime` | 112 | 0 / 79 | 30 / 31 |
| `F:\Programas\Radio y TV\Radio DJ\RadioDj V3\Plugin_SayTime` (pack de 2004) | 108 | 0 / 79 | 31 / 31 |
| `I:\Will Colina\Music\Tips Cactus FM\Efectos\Extras Dinesat\Hora Femenina en Off` | 107 | 0 / 79 | 31 / 31 |

Son grabaciones de locución válidas con la misma convención de nombres, pero **no son las tomas originales** que estaba
usando la emisora (difieren en tamaño y duración: `HRS00_O.mp3` original 47.950 B / 2 s frente a 36.679 B en `I:` y 18.389 B en `F:`).
El set de la emisora además incluye `HRS24.mp3`, que no existe en ninguno de los tres paquetes.

## Opciones pendientes de decisión

1. **Restaurar desde uno de los tres paquetes** (el de `I:` es el que está en la carpeta de tips de la emisora). Recupera
   locución real y coherente, pero serán otras tomas: cambian voces y duración respecto a lo que sonaba antes.
2. **Buscar en el respaldo de la emisora** (disco externo, nube, la otra máquina o el proyecto CactusFM original).
   Esta es la única vía para recuperar las tomas exactas.
3. **Borrar los 31 tonos de `bin/`** y dejar que `CustomTimeEngine` caiga en su respaldo real: si no encuentra el clip de
   hora, usa `TimeAnnouncementService.SpeakCurrentTime()` con `System.Speech`. Se escucha la hora sintetizada, no un tono.

Ninguna de las tres se ejecutó sin autorización. La opción 1 o 3 se pueden aplicar en minutos cuando decidas.

## Regla que queda para el resto del plan

Nunca generar fixtures con nombres que la app busca (`HRS*`, `MIN*`) ni escribir en carpetas que el `.csproj` copia a la
salida sin comprobar antes si ya existe un archivo con ese nombre. Ver `docs/PLAN_MEJORA_ARQUITECTURA.md`, hallazgo A18.
