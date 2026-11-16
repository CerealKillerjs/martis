# ADR-012 — Modelo metabólico v1 (T04)

- **Estado:** Propuesto (pendiente de validación de MARTIS Ciencia y decisión de MARTIS Lead), 2026-11-16.
- **Cierra:** la parte pendiente de [H-18](../TDD_HUECOS.md) (especie de cada depósito, composición de la biomasa y paso de kg a elementos).
- **Fuente:** TDD v1.0 §3.4, §5.4, §10.1; [ADR-011](ADR-011-unidades-del-modelo.md).

## Contexto

T04 pide reacciones con balance verificable (VAL-007), consumo ≤ disponibilidad (VAL-006), energía consumida ≤ disponible (VAL-008), `mass_balance.csv` por tick y un reparto entre poblaciones que no premie el orden de iteración. ADR-011 dejó sin fijar la especie química de cada depósito y la composición de la biomasa.

## Decisiones

### Especies (`data/reactions/metabolism.v1.json`)

Cada depósito de `ResourcePool` es una sola especie (`MODEL_ASSUMPTION`). La biomasa es materia orgánica de Redfield, en masa seca.

| Depósito | Fórmula |
| --- | --- |
| CarbonDioxide | CO2 |
| Water | H2O |
| Oxygen | O2 |
| Nitrogen | NH3 (nitrógeno fijado y accesible) |
| Phosphorus | H3PO4 (fosfato accesible) |
| Sulfur | S |
| Iron | Fe |
| OrganicCarbon | CH2O |
| MineralCarbon | C |
| Biomasa (`Population.BiomassKg`) | C106H263O110N16P (Redfield, masa seca; cierra R2-13) |

Las masas atómicas son los valores abreviados de IUPAC (Prohaska et al. 2022, doi:10.1515/pac-2019-0603), `SCIENTIFIC_MEASURED`, ámbito `model`. La masa molar de cada especie se deriva (`M_j = Σ_i A_ij · m_i`) y no se guarda.

### Reacciones

Coeficientes en mol por unidad de avance. El cargador comprueba `A·S = 0` en aritmética decimal exacta; una reacción desequilibrada es un error de carga con el id VAL-007.

| Id | Ecuación | Energía |
| --- | --- | --- |
| photoautotrophic_growth | 106 CO2 + 106 H2O + 16 NH3 + H3PO4 → Bio + 106 O2 | luz |
| aerobic_respiration | CH2O + O2 → CO2 + H2O | libera |
| heterotrophic_assimilation | 106 CH2O + 16 NH3 + H3PO4 → Bio | cuesta |
| necromass_breakdown | Bio → 106 CH2O + 16 NH3 + H3PO4 | ninguna |

`chem.energy.carbon_fixation` = 4,8·10⁵ J/mol C es `MODEL_ASSUMPTION` (orden de magnitud de la energía libre de fijar CO2 a CH2O).

### Ley de velocidad

Avance en mol C por tick:

`ξ_C = vmax · B · Δt · f_T · f_R · f_W · [f_I] · min_k(S_k / (K_k + S_k))`

y avance de la reacción `ξ = ξ_C / carbon_moles`. `f_T` es una gaussiana alrededor del óptimo, ensanchada por `ThermalTolerance`; `f_R = 1 / (1 + α · dosis_Sv · (1 − UvResistance))`; `f_W` es Monod en el agua líquida, con la semisaturación reducida por `DesiccationTolerance`; `f_I` es Monod en la irradiancia, escalado por `PhotosyntheticEfficiency` y solo en reacciones de luz. Una temperatura sin asignar es un error, no un factor 1. Todos los parámetros cinéticos son `GAME_BALANCE` en `data/organisms/organisms.v1.json`.

### Reparto

1. `resource-availability`: foto del depósito al empezar el tick.
2. `metabolism`: cada población pide `ξ` y se suma la demanda por celda y depósito en el `AllocationBuffer`.
3. `allocate-shared-resources`: si la demanda supera lo disponible, factor `disponible/demanda · (1 − 1e-12)`; cada flujo usa el mínimo de los factores de sus entradas. El factor es el mismo para todas las poblaciones de una celda, así que el orden no cambia el reparto.
4. `update-biomass-and-energy`: aplica los flujos; el balance hídrico sigue al depósito de agua; la abundancia es `B / masa individual`.
5. `validate-conservation`: VAL-006 (consumido ≤ foto inicial), VAL-007 (masa de cada elemento en cada celda dentro de `1e-12 + 1e-9·|M|` kg, contando la entrada externa de las inoculaciones) y VAL-008 (reserva de energía gastada ≤ reserva inicial + ganada). Una violación lanza `SimulationInvariantException` y el tick se deshace.

El pipeline con biología se llama `pipeline-v1`. El de Sprint 1 (`pipeline-v0`) no cambia.

## Consecuencias

- Mortalidad, reproducción, mutación, selección, ecología y reciclaje siguen siendo pasos vacíos hasta T05 y T06.
- Los valores `GAME_BALANCE` no son ciencia y se ajustan jugando. Las especies son una simplificación: no hay N2 atmosférico, sulfatos ni óxidos de hierro.
- Si Ciencia cambia una fórmula, el balance se vuelve a comprobar al cargar, así que un dato incoherente no llega a ejecutarse.
