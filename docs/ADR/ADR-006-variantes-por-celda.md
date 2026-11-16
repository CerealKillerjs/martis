# ADR-006 — DT-006: Límite de variantes por celda

- **Estado:** Propuesto — pendiente de visto bueno de Dani.
- **Decisión TDD:** DT-006 «Variantes por celda»; recomendación inicial: «Límite configurable 32».

## Contexto

Las variantes genéticas se representan dividiendo la población agregada (§4.2). Gestión: crear variante ante diferencia funcional significativa, fusionar equivalentes dentro de una tolerancia, eliminar poblaciones sin abundancia/biomasa, limitar variantes activas y conservar variantes raras con importancia evolutiva configurable (§4.4). «Límite inicial sugerido: 32 variantes por especie y celda. Es una decisión de rendimiento, no biológica.» La «explosión de variantes» es riesgo de probabilidad Alta e impacto Alto (§15). El objetivo de rendimiento (§7.4) es «máximo 32» y 10.000 poblaciones activas.

Inconsistencia de nombre (H-06): el texto dice «por especie y celda», pero `SimulationConfiguration.MaxVariantsPerCell` (§12.1) sugiere un tope por celda en total.

## Opciones

1. **Tope configurable por especie y celda, valor por defecto 32.**
2. Tope global por celda (suma de especies).
3. Sin tope, con fusión por tolerancia únicamente. Riesgo de explosión.

## Decisión recomendada

Opción 1, valor por defecto 32 clasificado como `GAME_BALANCE` (límite de rendimiento, no biológico), renombrando la propiedad a `MaxVariantsPerSpeciesPerCell`. Política de fusión/poda determinista: orden por `PopulationId`, criterio y tolerancia como parámetros etiquetados; nunca crear ni destruir masa al fusionar (la abundancia y biomasa se suman; ADR-000 R3).

## Consecuencias

- Límite acotado ⇒ memoria y tiempo acotados en el peor caso.
- La fusión/poda altera la dinámica evolutiva: debe ser observable (evento/estadística) y reproducible.
- Conservar variantes raras compite con el tope; la regla de desempate debe estar definida.

## Cómo se verifica

- Test: ninguna celda/especie supera el límite tras `MutationSystem`/`EvolutionSystem`.
- Test: fusionar variantes conserva abundancia y biomasa totales (suma exacta dentro de tolerancia).
- Test: mismo seed ⇒ misma secuencia de fusiones/podas (VAL-010).
- Property-based (§13): abundancia nunca negativa tras fusión/poda.
- Benchmark `MutationUpdate` con y sin tope (medición).
