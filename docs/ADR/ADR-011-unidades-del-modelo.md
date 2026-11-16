# ADR-011 — Unidades del modelo

- **Estado:** Propuesto. Lo valida MARTIS Ciencia. Decisión de MARTIS Lead, 2026-11-16, pendiente de esa validación.
- **Cierra:** [H-05](../TDD_HUECOS.md) y [H-18](../TDD_HUECOS.md).
- **Fuente:** TDD v1.0 §2.2, §2.3, §2.5, §3.1, §3.2, §10.1.

## Contexto

H-05: el paso de tiempo del TDD es 1 sol y el campo de edad se llamaba en días. Un sol no es un día terrestre. H-18: `ResourcePool` no tenía unidad ni matriz de composición. El agua aparecía a la vez como depósito (`Water`) y como estado ambiental (`WaterIce`, `WaterVapor`) sin una frontera comprobable.

## Decisiones

### Tiempo

1 tick = 1 sol = 88 775 s. La cifra está solo en `MartianTime.SecondsPerSol`. No se añaden decimales ni un enlace: la cita exacta del NSSDC sigue pendiente de MARTIS Ciencia. En el código no existe `DeltaTimeDays`. La edad media de la población es `AgeDistributionMeanSols`, en soles. Cuando llegue la configuración, su paso también irá en soles.

### Depósitos

Los nueve campos de `ResourcePool` son masa en **kg por celda**. La masa molar que los pase a cantidad de sustancia vive en la matriz A (`ElementalCompositionMatrix`) y se clasifica `MODEL_ASSUMPTION`. Esta entrega no trae esos números.

El buffer de asignación usa la misma unidad. Sigue sin consumir el depósito.

### Agua

Tres compartimentos, una sola masa contable por celda:

| Compartimento | Dónde | Qué es |
| --- | --- | --- |
| Líquido accesible | `ResourcePool.Water` | El único agua que la biología puede usar. kg por celda. |
| Hielo | `EnvironmentState.WaterIce` | Reserva física. La biología no la consume. kg por celda. |
| Vapor | `EnvironmentState.WaterVapor` | Reserva física. La biología no la consume. kg por celda. |

No hay un segundo campo de líquido en el ambiente ni de hielo o vapor en el depósito. La misma agua no se guarda en dos compartimentos.

`CellWaterBudget.RecordedTotalKg` no es un cuarto stock. Tiene que ser igual a la suma `Water + WaterIce + WaterVapor`, en ese orden (`CellWaterBudget.ComponentSum`). Si la misma masa se anota en el líquido y en el hielo y el total registrado no crece, la suma deja de cuadrar y el validador emite **DOM-VAL-012**. Cada componente, y el total, tienen que ser finitos y ≥ 0. Transferir agua entre compartimentos conservando la masa es un proceso físico de T03/T04: no está implementado.

### Fenotipo

`MaintenanceCost` y `ReproductionRate` son adimensionales por tick, clasificación `MODEL_ASSUMPTION`, hasta fijar la escala. El resto de componentes del fenotipo no tiene unidad todavía.

### Ambiente

Temperatura en K, estrictamente > 0 cuando está asignada. Presión en Pa, ≥ 0 cuando está asignada. El valor por defecto de las dos es **unset**, distinto de 0: 0 K no es un dato válido y 0 Pa sí lo es. Un escenario cargado no puede dejarlas sin asignar, ni dejar la celda sin `Scope` (`PlanetaryMean`, `Regional(SiteId)`, `Lab` o `Game`).

Estos siete campos llevan unidad y clasificación `MODEL_ASSUMPTION`. No son mediciones:

| Campo | Unidad |
| --- | --- |
| UvRadiation | W/m² en superficie |
| IonizingRadiation | W/m² en superficie (flujo de energía; no se elige unidad dosimétrica) |
| SolarIrradiance | W/m² en superficie, no en el tope de la atmósfera |
| WaterIce | kg por celda |
| WaterVapor | kg por celda |
| SoilPorosity | fracción adimensional en [0, 1] |
| PerchlorateConcentration | kg de perclorato por celda, no una concentración molar |

La irradiancia solar del campo es de superficie. W/m² aquí significa J/(s·m²), a partir de las unidades de energía, tiempo y longitud del TDD §2.2.

### Rejilla

El ejemplo del prototipo es 64×64 celdas. `GridIndex` no fija ese tamaño. El tamaño físico de la celda sigue abierto (H-07): no hay metros por celda y no hay difusión.

## Cómo se verifica

- `MartianTimeTests` exige que `88775` aparezca en un solo fichero de `src/`.
- Porosidad: 0 y 1 pasan; un valor por encima de 1, incluido el siguiente `double` representable, no.
- 0 K asignado se rechaza. Unset no es igual a 0 asignado. Un mundo vacío pasa el validador; el mismo mundo como escenario cargado no.
- DOM-VAL-012: líquido 5 kg, hielo 5 kg y total registrado 5 kg es una violación. Líquido 2, hielo 3, vapor 4 y total 9 no lo es.
- Los ids VAL-001, VAL-002, VAL-003 y VAL-005 se reservan al texto de TDD §10. La reserva de energía, la edad, la viabilidad, la porosidad, el ambiente, el buffer, el alcance, la temperatura, la presión y el agua usan DOM-VAL.

## Pendiente de MARTIS Ciencia

Validar este ADR. Completar la cita del sol, las masas molares de la matriz A y, si hace falta, sustituir el flujo W/m² de la radiación ionizante por una unidad dosimétrica.
