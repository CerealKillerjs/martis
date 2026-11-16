# Registro de decisiones de arquitectura (ADR) — MARTIS

Formato MADR breve: Título, Estado, Contexto, Opciones, Decisión recomendada, Consecuencias, Cómo se verifica.

**Estados:** *Propuesto* (recomendación redactada, pendiente de visto bueno de Dani) · *Aceptado* · *Reemplazado por ADR-NNN* · *Rechazado*.
**Todos los ADR están hoy en *Propuesto*** y quedan pendientes del visto bueno de Dani. Cambiar un estado a *Aceptado* se hace en un PR que cite la aprobación.

Fuente de las decisiones: `docs/MARTIS_TDD_v1.0.txt`, §17 (DT-001..DT-010) y reglas de §0, §1, §3, §6, §10, §21.

| ADR | Decisión | Recomendación | Estado |
|---|---|---|---|
| [ADR-000](ADR-000-reglas-de-arquitectura.md) | Reglas de arquitectura (dominio puro, sin ciclos, sin recursos gratis, determinismo) | R1–R6 del TDD | Propuesto (cerrado en TDD §21; falta visto bueno) |
| [ADR-001](ADR-001-version-godot.md) | DT-001 Versión de Godot 4 | 4.7.2-stable .NET, provisional; fijar definitiva en T08 | Propuesto |
| [ADR-002](ADR-002-version-dotnet.md) | DT-002 Versión .NET | SDK 10 LTS en `global.json`; libs `net10.0` | Propuesto |
| [ADR-003](ADR-003-prng.md) | DT-003 PRNG | PCG32 `pcg32-v1`, 5 flujos | Propuesto |
| [ADR-004](ADR-004-formato-snapshots.md) | DT-004 Formato de snapshots | Binario versionado; compresión si se mide; JSON solo export | Propuesto |
| [ADR-005](ADR-005-modelo-espacial.md) | DT-005 Modelo espacial | Arrays lineales `y·W+x` | Propuesto |
| [ADR-006](ADR-006-variantes-por-celda.md) | DT-006 Variantes por celda | Límite configurable, 32 por especie y celda | Propuesto |
| [ADR-007](ADR-007-simulacion-paralela.md) | DT-007 Simulación paralela | No en la primera versión | Propuesto |
| [ADR-008](ADR-008-difusion.md) | DT-008 Difusión | Diferencias finitas explícitas con control de estabilidad | Propuesto |
| [ADR-009](ADR-009-integracion-genetica.md) | DT-009 Integración genética | Herencia clonal + mutación | Propuesto |
| [ADR-010](ADR-010-plataforma-inicial.md) | DT-010 Plataforma inicial | Windows + Linux; sin Web (verificado vigente) | Propuesto |

Huecos y contradicciones del TDD que afectan a estos ADR: [`../TDD_HUECOS.md`](../TDD_HUECOS.md).

## Plantilla

```markdown
# ADR-NNN — Título

- **Estado:** Propuesto | Aceptado | Reemplazado por ADR-NNN | Rechazado
- **Decisión TDD:** (si procede)

## Contexto
## Opciones
## Decisión recomendada
## Consecuencias
## Cómo se verifica
```
