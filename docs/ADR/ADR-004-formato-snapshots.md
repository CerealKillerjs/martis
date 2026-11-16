# ADR-004 — DT-004: Formato de snapshots

- **Estado:** Aceptado (Dani, 2026-11-16). Se aprueba la decisión recomendada.
- **Decisión TDD:** DT-004 «Formato snapshots»; recomendación inicial: «Binario versionado + compresión».

## Contexto

SQLite es la persistencia principal; la tabla `Snapshot` guarda `Payload BLOB` (§9.2). El contenido de un snapshot de guardado incluye tick, estado ambiental, poblaciones, genomas, recursos, estadísticas acumuladas, estado del PRNG y versiones de motor/datos (§9.3). Al cargar: metadatos → compatibilidad → último snapshot válido → checksum → deserializar → restaurar RNG → validar invariantes → reanudar; si está corrupto, se intenta el anterior.

Además el TDD usa «snapshot» para dos cosas distintas (hueco H-03): el **snapshot de vista** para la UI (`SimulationSnapshot`, §8.1, solo agregados) y el **snapshot de guardado** (estado completo). Este ADR decide el formato del segundo y del export de depuración del CLI.

## Opciones

1. **Binario propio versionado** (cabecera con magic, versión de esquema, versión de motor, id de algoritmo PRNG, longitud y checksum; cuerpo con escritura explícita de campos). Doubles guardados como bits IEEE-754 para ida y vuelta exacta. Compresión BCL (GZip/Brotli) solo si el benchmark lo justifica (§9.3).
2. **JSON canónico**. Legible y útil para depuración; mayor tamaño y riesgo de pérdida de precisión si no se serializa con ida y vuelta exacta.
3. **Formato con librería de terceros** (MessagePack, protobuf…). Añade dependencia no prevista en el TDD; requeriría su propio ADR.

## Decisión recomendada

**Opción 1 para guardado** (BLOB en `Snapshot.Payload`) y **JSON canónico (orden estable, cultura invariante) solo para la exportación del CLI** (`final_snapshot.json`) y la depuración. Los JSON de definiciones de §9 no cambian. El checksum es SHA-256 sobre el cuerpo sin comprimir. La compresión queda **condicionada a medición** (H-10 cerrado: §9.3 dice «si el benchmark lo justifica»; §17 dice «+ compresión»).

## Consecuencias

- Hay que mantener un lector por cada versión de esquema soportada o una migración; «compatibilidad de carga no equivale a reproducibilidad histórica» (§9.3).
- El orden de escritura de colecciones debe ser canónico (ADR-000 R4).
- El formato JSON de exportación no se usa para reanudar partidas.

## Cómo se verifica

- Round-trip: `Restore(CreateSnapshot())` produce un estado idéntico bit a bit (VAL-009).
- Continuar N ticks desde el estado restaurado == continuar desde el original (VAL-009 + VAL-010).
- Test de corrupción: alterar un byte ⇒ checksum falla ⇒ carga el snapshot anterior.
- Test de versión: un payload con esquema desconocido se rechaza con error claro.
- Archivo «golden» pequeño versionado en `tests/` para detectar cambios accidentales de formato.
- Tamaño y tiempo de snapshot completo se **miden** (BenchmarkDotNet); el objetivo «< 1 s» de §7.4 es objetivo, no medición.

## Nota de aprobación (2026-11-16)
Dani aprueba binario versionado con cabecera para el guardado, y JSON solo para la exportación del CLI y la depuración. Los JSON de definiciones de §9 no cambian. El checksum es SHA-256 sobre el cuerpo sin comprimir. La compresión forma parte del formato objetivo; se activa cuando el benchmark la mida.
