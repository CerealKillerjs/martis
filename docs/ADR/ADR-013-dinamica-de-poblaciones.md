# ADR-013 — Dinámica de poblaciones v1 (T05)

- **Estado:** Propuesto (pendiente de MARTIS Ciencia y MARTIS Lead), 2026-11-16.
- **Fuente:** TDD v1.0 §3.5, §5.4; [ADR-012](ADR-012-modelo-metabolico.md).

## Contexto

El TDD §3.5 pide crecimiento logístico acotado por `K = min(K_W, K_C, K_N, K_P, K_E)` sin crear recursos, y mortalidad combinada `m = 1 − Πᵢ(1 − mᵢ)`. En MARTIS el crecimiento ya es metabolismo (ADR-012): cada kg nuevo sale del depósito. T05 añade lo que lo frena y lo que devuelve la materia.

## Decisiones

### Crecimiento

No hay una segunda ecuación logística que sume biomasa: el término `r·N·(1 − N/K)` se obtiene con el metabolismo, limitado por Monod, más el término de competencia de abajo. Así la capacidad no puede crear materia.

### Mortalidad (`calculate-mortality`)

Cada término es una probabilidad por tick, `mᵢ = 1 − exp(−rᵢ · estrésᵢ · Δt)`:

| Término | Estrés | Parámetro (`GAME_BALANCE`) |
| --- | --- | --- |
| fondo | 1 | `base_mortality` (1/sol) |
| térmico | `1 − f_T` | `thermal_mortality` (1/sol) |
| desecación | `1 − f_W` | `desiccation_mortality` (1/sol) |
| radiación | dosis Sv/sol · (1 − UvResistance) | `radiation_mortality` (1/Sv) |
| inanición | fracción del mantenimiento sin pagar | `starvation_mortality` (1/sol) |
| competencia | `1 − K/B_celda` si `B_celda > K` (término directo, no exponencial) | `carrying_capacity` (kg, K_space) |

`K = min(K_space, K_C, K_N, K_P)`, donde `K_X` es la biomasa que habría si todo el elemento X de la celda (depósito y biomasa viva) fuera biomasa. H y O se excluyen (el agua y el O2 ya limitan por Monod). `K_E` no se modela aparte: la energía actúa por la inanición.

El mantenimiento cuesta `maintenance_power · B · Δt` J y se paga de la reserva (cuenta en VAL-008). La biomasa muerta pasa a la necromasa de la celda. Una población con menos de un individuo (`B < masa individual`) se extingue: se quita de la lista y su biomasa también es necromasa. El paso no usa el azar; el ruido demográfico es deriva (T06).

### Reproducción (`generate-reproduction`)

En el modelo agregado, la biomasa nueva son individuos nuevos (`N = B / masa individual`). El paso cuenta los recién nacidos (`N − N₀·(1 − m)`) y actualiza la edad media: los supervivientes envejecen Δt y los recién nacidos entran con edad 0.

### Reciclaje (`recycle-dead-biomass`)

Toda la necromasa del tick pasa por `necromass_breakdown` (Bio → 106 CH2O + 16 NH3 + H3PO4) en el mismo tick. VAL-007 cuenta la necromasa pendiente como masa de la celda, así que el balance cierra en cada paso.

### Versión

El pipeline con biología pasa a `pipeline-v2`. Un snapshot de `pipeline-v1` se puede leer, pero su continuación ya no es la misma.

## Consecuencias

- Con un fotótrofo solo, el agua acaba atrapada en CH2O y la población colapsa en unos 100–200 soles en MarsBaseline. Es lo esperado sin descomponedor: el ciclo se cierra en T09 con la respiración aerobia (CH2O + O2 → CO2 + H2O).
- `mass_balance.csv` añade `maintenance_j`, `unpaid_maintenance_j`, `biomass_died_kg`, `biomass_recycled_kg` y `extinctions`.
- Los valores son de juego y se ajustan jugando, no son mediciones.
