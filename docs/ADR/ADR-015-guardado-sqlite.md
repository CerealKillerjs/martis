# ADR-015 — Guardado en SQLite, formato binario y recuperación (T07)

- **Estado:** Propuesto (pendiente de MARTIS Lead), 2026-11-16.
- **Desarrolla:** [ADR-004](ADR-004-formato-snapshots.md). Cierra [H-14](../TDD_HUECOS.md).
- **Fuente:** TDD v1.0 §9.1–9.3.

## Decisiones

### Formato binario (`StateSnapshotBinary`)

Little-endian. La cabecera tiene 48 bytes:

| Campo | Tamaño | Valor |
| --- | --- | --- |
| magia | 4 | `MRTS` |
| versión de formato | u16 | 1 |
| flags | u16 | 0 (el bit 0 se reserva para la compresión, que se activará cuando el benchmark lo justifique) |
| longitud del cuerpo | i64 | bytes que siguen |
| SHA-256 del cuerpo | 32 | sin comprimir |

El cuerpo guarda el `StateSnapshot` en el mismo orden canónico que el JSON. Las cadenas van en UTF-8 con prefijo de longitud, los doubles como sus bits IEEE y los valores opcionales con un byte de presencia. Una cabecera, una longitud o un checksum que no cuadran lanzan `SnapshotCorruptException`.

### Esquema de estado v2

`martis-state-v2` añade `next_population_id`, de modo que un id no se reutiliza tras una extinción (la deuda de ADR-014). El lector JSON acepta todavía `martis-state-v1` y deriva el id. El formato binario solo escribe v2.

### Migraciones

Cada migración tiene un número y es inmutable una vez publicada. Se registran en la tabla `SchemaMigration` y en `PRAGMA user_version`, y cada una se aplica en su propia transacción. Abrir un archivo de una versión más nueva es un error.

1. `tdd-9.2-initial`: el SQL del TDD §9.2 tal cual.
2. `snapshot-checksum-and-versions`: `Snapshot.StateSchema`, `Snapshot.Checksum` (SHA-256 del payload), `SaveGame.ScenarioId` y `SaveGame.PipelineVersion`.

Las tablas `CellState`, `PopulationState`, `GenomeState`, `Experiment`, `ExperimentResult` y `WorldConfiguration` del §9.1 todavía no existen. El snapshot binario ya contiene ese estado; se añadirán como migraciones cuando haga falta consultarlo por SQL.

### Semilla (H-14)

`WorldSeed` es `ulong` y la columna SQLite es `INTEGER` con signo. Se guardan los bits (`unchecked((long)seed)`), de modo que cualquier semilla vuelve igual, incluida `ulong.MaxValue`.

### Recuperación

`LoadLatestValid` recorre los snapshots del más nuevo al más viejo y salta los que tienen:

- un checksum de la fila que no coincide;
- un payload que ya no es BLOB;
- un payload que el decodificador del llamador rechaza.

Devuelve el primero bueno y la lista de ticks rechazados. El CLI avisa de cada uno.

### CLI

`simulate … --save <db> [--save-name] [--snapshot-interval k]` crea una partida y la guarda en tres momentos:

- en el tick 0, si no hay inoculaciones en cola;
- cada `k` ticks;
- al final.

Los eventos `mutation` y `extinction` van a `SimulationEvent`. `simulate --scenario <id> --save <db> --resume <id> --ticks N --output <dir>` reanuda desde el último snapshot válido. La semilla se toma de la partida, y si el pipeline no coincide se rechaza.

## Verificación

- Guardar 30 ticks, reabrir y continuar 20 da el mismo snapshot que 50 ticks seguidos, con la semilla `ulong.MaxValue`.
- Corromper el snapshot del tick 30 hace que se continúe desde el 20, y el resultado coincide con el de 40 ticks seguidos.
