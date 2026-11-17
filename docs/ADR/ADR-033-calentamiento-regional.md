# ADR-033 — Calentamiento regional: calefactores y anomalía térmica (E1)

- **Estado:** Propuesto, 2026-11-17. Los valores son GAME_BALANCE o MODEL_ASSUMPTION y están pendientes de ajuste jugando.
- **Contexto:** La fase E del Documento Maestro v2.0 (§10, §31) empieza la terraformación regional. El documento pide que no sea una barra de progreso: cada intervención cuesta energía y mantenimiento, tiene límites y puede fallar o tener efectos adversos. La terraformación global queda como capa especulativa (Jakosky y Edwards 2018); el MVP es regional y biológico. E1 es la primera intervención: calentar el suelo alrededor de una máquina. Se apila sobre el corte vertical ([ADR-031](ADR-031-robots-e-instalaciones.md), [ADR-032](ADR-032-valle-y-guardado-completo.md)).

## Decisiones

### El motor solo recibe órdenes validadas

`SetCellTemperatureCommand(celda, kelvin)` es una orden nueva de `Martis.Contracts`. `SetCellTemperatureHandler` la aplica en el límite del tick, como cualquier otra orden, y rechaza celdas fuera del mundo o temperaturas no finitas o fuera de 1–400 K. El sistema de entorno v0 sigue leyendo la temperatura sin cambiarla, así que el calentamiento no toca la física del motor: solo cambia su dato de entrada.

### El clima regional vive junto a la sesión

`RegionalClimate` (`Martis.Simulation.Terraforming`) guarda la temperatura de base de cada celda y una anomalía térmica. `GameSession.AttachClimate` lo conecta. Antes de cada sol, `GameSession.Advance` le pide un paso y entrega al motor una orden por celda que cambia:

- Cada calefactor que funciona fuerza la anomalía hasta `peak_delta_k` (30 K) en su celda, con caída lineal hasta cero a `radius_cells` (3 casillas). Los forzamientos se suman.
- La anomalía se relaja hacia ese forzamiento con la constante `relaxation_sols` (4 soles): ΔT ← ΔT + (F − ΔT)(1 − e^(−1/τ)). El calor tarda en llegar y tarda en irse.
- Ninguna celda pasa de `max_temperature_k` (320 K).

### Costes, límites y fallos

- Un calefactor va sobre suelo medido, cuesta `battery_cost` (20 %) de batería al rover y consume `power_w` (150 W) del mismo módulo que alimenta las estaciones (300 W). Por eso un calefactor deja sitio para una sola estación, y al revés: hay que elegir entre medir y calentar.
- Se desgasta `wear_per_sol` (2,5 %) cada sol. Al llegar al 100 % se avería, deja de consumir y la bitácora lo anuncia. El calor se disipa en pocos soles.
- El rover lo repara con X a menos de 2 casillas. X repara el calefactor o la estación más desgastada al alcance.
- Calentar no garantiza nada: el calor favorece a unos organismos y perjudica a los adaptados al frío. Lo decide la biología del motor, no una regla del calefactor.

### Juego

- **H** en exploración monta un calefactor bajo el rover. La orden queda en el registro con su resultado.
- En el terreno, el suelo caliente se oscurece y brilla en tonos infrarrojos, más de noche. Los calefactores tienen aletas que brillan con el calor que dan y una luz que avisa del desgaste.
- La vista planetaria muestra el calentamiento medio, las celdas calentadas y los calefactores activos.
- Acciones de tutorial: `build-heater`, `repair-heater`.
- El clima se guarda dentro del estado de expedición (`ExpeditionState.Climate`). Las partidas anteriores no lo tienen y empiezan sin intervenciones. Al cargar, la base es la temperatura guardada menos la anomalía, así que el calor sigue donde estaba.

## Consecuencias

- Hay una primera palanca regional con coste, límite y fallo, sin tocar la física del motor.
- El modelo es deliberadamente simple: no hay conducción lateral real ni balance radiativo. Un modelo climático regional más completo sustituirá a `RegionalClimate` sin cambiar la orden del motor.
- Siguientes hitos de la fase E: agua (extracción de hielo y cúpulas), energía (solar con polvo), métricas regionales con una misión de terraformación, y `PlanetDefinition` (ARC-003).
