# Cómo colaborar en MARTIS

Gracias por sumarte. Esta guía resume cómo se trabaja en el repositorio. El idioma del proyecto es el **español**: documentación, PRs, mensajes de commit y textos del juego. Los identificadores y comentarios del código van en inglés.

## Antes de empezar

1. Lee el [README](README.md) para compilar, probar y jugar.
2. Lee [docs/ESTADO.md](docs/ESTADO.md): qué está hecho, qué falta y los pendientes conocidos.
3. Ten a mano el [Documento Maestro v2.0](docs/maestro/MARTIS_Documento_Maestro_v2.0.docx). Es la guía de diseño y arquitectura. Si una tarea contradice el documento, se habla antes de programarla.
4. Repasa las ADR que toquen tu área ([docs/ADR](docs/ADR/README.md)), empezando por [ADR-000](docs/ADR/ADR-000-reglas-de-arquitectura.md), que fija las reglas de arquitectura.

## Flujo de trabajo

- **`main` siempre compila y pasa los tests.** No se empuja directamente a `main`.
- **Una rama y un PR por hito o tarea.** Nombres orientativos: `feature/<hito>-<tema>` o `fix/<tema>`, por ejemplo `feature/e2-agua-y-hielo`. Las ramas `claude/*` son las que crea el asistente de IA.
- Si una tarea depende de otra que aún no está en `main`, el PR se apila sobre la rama anterior (base = esa rama) y se fusionan en orden. Evita pilas largas: fusiona en cuanto un hito esté revisado.
- Abre el PR como **borrador** mientras trabajas y pásalo a «listo para revisión» cuando cumpla la [lista de revisión](docs/PR_CHECKLIST.md).
- La descripción del PR dice qué cambia, por qué, cómo se ha probado (comandos y resultado) y qué queda pendiente. Si hay capturas del juego, adjúntalas.
- Fusión con **commit de merge** (no squash) para conservar el historial de los hitos.

## Antes de pedir revisión

```bash
dotnet build MARTIS.sln -c Release          # 0 avisos (los avisos cuentan como fallo en revisión)
dotnet test MARTIS.sln -c Release
dotnet build src/Martis.Game/Martis.Game.csproj
godot --headless --path src/Martis.Game -- --smoke
godot --headless --path src/Martis.Game -- --title-smoke
```

- Si añades scripts de Godot, ejecuta `godot --headless --path src/Martis.Game --import` y sube los `.uid` que genera.
- Si cambias dependencias NuGet, sube los `packages.lock.json`: la CI restaura en `--locked-mode`.
- Si cambias un resultado de referencia (vectores dorados, huellas), justifícalo en el PR.

## Reglas que no se negocian

Las comprueban los tests de arquitectura. El detalle está en el [README](README.md#reglas-que-comprueban-los-tests) y en ADR-000:

- **Determinismo.** Nada de `System.Random`, `Guid.NewGuid()`, `DateTime.Now`, `Task.Run` ni `Parallel.*` en `src/`. El azar sale de los flujos `pcg32-v1`.
- **Sin recursos gratis.** La masa y la energía se conservan; las reacciones están balanceadas.
- **Sin datos inventados.** Cada número del modelo lleva fuente o la clasificación `MODEL_ASSUMPTION`, `GAME_BALANCE` o `SPECULATIVE`.
- **El motor no conoce Godot.** El juego habla con la simulación solo a través de `GameSession`. Lo que no sea de Godot (modos, órdenes, tiempo) va en `Martis.Application`, para poder probarlo.
- **Poblaciones agregadas.** No hay individuos como objetos de simulación.
- **Datos versionados.** Un cambio incompatible en `data/` crea un fichero `*.vN+1.json` nuevo en lugar de reescribir el anterior.

## Decisiones (ADR)

Una decisión de diseño o arquitectura que afecte a más de un fichero lleva su ADR en `docs/ADR/ADR-NNN-titulo.md`, con estado *Propuesto*. La aprueba Daniel. Usa el siguiente número libre (el último es ADR-039) y añádela al [índice](docs/ADR/README.md). Un hueco o una deuda que necesite decisión va a [docs/TDD_HUECOS.md](docs/TDD_HUECOS.md) como H-nn (el último es H-37).

## Dónde va cada cosa

| Quiero… | Dónde |
| --- | --- |
| un sistema nuevo de simulación | `src/Martis.Simulation/<Área>/` y su test en `tests/Martis.Simulation.Tests` |
| exponer algo al juego | método o vista en `GameSession` (`src/Martis.Simulation/Sessions`) |
| lógica de juego sin Godot | `src/Martis.Application` y `tests/Martis.Application.Tests` |
| una pantalla, un control o un efecto visual | `src/Martis.Game/scripts`, `scenes`, `shaders` |
| una tecla o acción nueva del juego | `GameRoot` y, si es una acción de partida, el menú Esc (`PauseMenu.cs`), no la barra superior (ADR-037) |
| misiones, organismos, campaña, economía | `data/*/*.v1.json` (+ test de carga) |
| una región de Marte nueva | `tools/planet/build_region.py` → `data/planets/mars/<sección>` y `data/planets/mars.v1.json` |
| música o efectos | `src/Martis.Audio` (ADR-018) |

## Ficheros locales que no se suben

`.gitignore` ya excluye `bin/`, `obj/`, `.godot/`, `results/` y las cachés de Python. También excluye `src/Martis.Game/addons/` y `src/Martis.Game/demo/`: ahí cada uno puede instalar plugins del editor (Terrain3D, TerraBrush, servidores MCP de Godot, etc.) sin que entren en el repositorio. Si un plugin llega a ser necesario para el juego, se decide en una ADR y se añade de forma explícita.
