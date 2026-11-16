# ADR-016 — Ecosistema MVP y misión FIRST COLONY (T09)

- **Estado:** Propuesto (pendiente de MARTIS Ciencia, MARTIS Lead y Dani por H-19), 2026-11-16.
- **Fuente:** TDD v1.0 §16 (T09), B.5, B.6; [ADR-012](ADR-012-modelo-metabolico.md)–[ADR-014](ADR-014-evolucion.md).

## Decisiones

### Tres arquetipos (`data/organisms/organisms.v1.json`)

| Id | Arquetipo | Reacciones | Rasgos |
| --- | --- | --- | --- |
| `phototroph_a01` | fotótrofo | `photoautotrophic_growth` | genoma A01 del TDD §4.1 |
| `decomposer_d01` | descomponedor | `heterotrophic_assimilation` + `aerobic_respiration` | crece con CH2O, NH3 y H3PO4; obtiene la energía respirando CH2O con O2 (40 % a la reserva) y paga 1·10⁵ J/mol C por asimilar |
| `extremophile_e01` | extremófilo | `photoautotrophic_growth` | óptimo de 215 K, más tolerancia a la UV y a la sequía, crece más despacio |

Todos son organismos sintéticos y sus parámetros son `GAME_BALANCE`.

### Ciclo cerrado

Sin descomponedor, el agua y el carbono quedan atrapados en CH2O y la colonia colapsa (ADR-013). El descomponedor los devuelve como CO2 y H2O. En MarsBaseline, con la semilla 5, A01 (0,5 kg) y D01 (0,05 kg) se estabilizan en unos 2,2 kg durante 400 soles. A01 solo baja de 0,01 kg.

### Diseño del jugador

`IntroducePopulationCommand.GeneOverrides` (en el CLI, `--introduce org@x,y:kg+GEN=valor`) sustituye genes del genoma base. Un gen desconocido o un valor fuera de [0, 1] es un error.

### Misión (`data/missions/first_colony.v1.json`)

| Objetivo | Se cumple cuando |
| --- | --- |
| `analyze_region` | el anfitrión informa de la acción `analyze-region` |
| `design_phototroph` | vive un fotótrofo cuyo genoma difiere del base y no nació por mutación |
| `run_experiment` | el anfitrión informa de la acción `run-experiment` |
| `deploy_colony` | vive al menos un fotótrofo |
| `sustain_generations` | la biomasa fotótrofa total es ≥ 0,1 kg durante 200 soles seguidos |
| `observe_mutations` | aparece al menos una variante nueva |

Los umbrales son `GAME_BALANCE` con estado `PROPOSED`: H-19 sigue abierto hasta que Dani y Ciencia los confirmen. En el juego, el análisis y el laboratorio son acciones de la interfaz (T08). En el CLI se informan con `--mission-action`. `MissionTracker` vive fuera del snapshot: si se reanuda una partida, la racha de soles empieza de cero.

Con la semilla 5, un fotótrofo diseñado (`UV_PROTECTION=0.85`) y D01, la misión se completa en el tick 200.

## Consecuencias

- Un diseño más agresivo, por ejemplo `PHOTOSYNTHESIS=0.8`, crece más rápido, agota el agua antes de que el descomponedor la recicle y colapsa. Es un dilema de diseño interesante para el jugador, pero hay que ajustarlo jugando.
- MarsBaseline v0 es uniforme (las 4096 celdas son iguales), así que el extremófilo todavía no tiene un nicho propio. Hace falta un escenario con gradientes (frío, sombra, radiación) para que la elección de organismo importe.
