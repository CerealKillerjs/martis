# ADR-017 — Juego 3D en Godot 4.7.2 sobre `GameSession` (T08)

- **Estado:** Propuesto (pendiente de Dani), 2026-11-16.
- **Fuente:** TDD v1.0 §1.1, §8, §16 (T08), B.6; ROADMAP T08; [ADR-001](ADR-001-version-godot.md), [ADR-002](ADR-002-version-dotnet.md), [ADR-015](ADR-015-guardado-sqlite.md), [ADR-016](ADR-016-ecosistema-y-first-colony.md).
- **Petición de Dani (2026-11-16):** «la idea es que el juego sea visualmente bastante llamativo; si puede ser en 3D, mejor».

## Contexto

El ROADMAP pide para T08 un mapa 64×64 «como textura generada desde datos de celdas», reloj, estadísticas, pausa y velocidad, y que Godot solo use comandos y copias inmutables. Dani pidió un juego vistoso y, si es posible, en 3D. La simulación sigue siendo una rejilla 2D de 64×64 celdas.

## Decisiones

### Versión de Godot y TFM

- **Godot 4.7.2-stable (.NET)** con `Godot.NET.Sdk/4.7.2` y `TargetFramework` **`net10.0`**. Comprobado el 2026-11-16: el SDK compila el juego contra las librerías `net10.0` del motor, y el ejecutable oficial `Godot_v4.7.2-stable_mono_linux` lo carga y lo ejecuta (modo headless y Vulkan). Esto resuelve el «pendiente de verificar» de ADR-001: no hace falta esperar a 4.8.
- `src/Martis.Game/Directory.Build.props` sustituye al de la raíz para el juego: sin `TreatWarningsAsErrors` ni gestión central de paquetes (Godot.NET.Sdk añade sus propios paquetes), con lockfile y restore bloqueado en CI como el resto.
- `Martis.Game` sigue **fuera de `MARTIS.sln`** (H-17). La CI lo compila en Linux con `dotnet build src/Martis.Game/Martis.Game.csproj` (el SDK de Godot viene de NuGet; no hace falta el editor).

### Frontera con el motor

- `Martis.Simulation.Sessions.GameSession` es la única puerta: `NewGame`, `Resume`, `Introduce`, `Advance`, `Experiment` (laboratorio sobre una copia), `RecordAction`, `View` (`WorldView`, arrays copiados), `Inspect` (`CellReport`), `Capture` (snapshot) y un registro de eventos con celda (`SessionEvent`).
- El juego referencia Simulation y Persistence, como el CLI. `GameBoundaryTests` falla si un script usa `GameSession.Engine`, `Workspace`, `SimulationState`, `SimulationEngine`, `MetabolismWorkspace` o `Martis.Domain`.
- La simulación corre en el hilo principal, como mucho un sol por fotograma (a 8 soles/s un tick de ~10 ms cabe). ADR-000 R4 prohíbe `Task.Run` en `src`; así ningún nodo se toca desde otro hilo.

### Representación 3D

| Elemento | Cómo | Datos que usa |
| --- | --- | --- |
| Relieve | malla procedural (4 vértices por celda) con ruido fBm, mesetas, dunas y cráteres a partir de la semilla del mundo | solo la semilla: **es decorativo**, la simulación no tiene altura |
| Suelo | shader `terrain.gdshader`: regolito por altura y pendiente, ondulaciones en el mapa de normales | — |
| Capas de datos | textura 64×64 `Rgbaf` (temperatura, agua, radiación, biomasa) pintada de forma emisiva con rejilla de celdas | `WorldView` |
| Tapetes microbianos | patrón celular verde (fotótrofos) y violeta (descomponedores), tono por variante, halo difuminado de 3 celdas | `WorldView` (el halo es visual; no hay dispersión simulada) |
| Colonias | cúpulas bioluminiscentes (`MultiMesh`, hasta 3 por celda según variantes), tamaño por log de la biomasa | `WorldView` |
| Cielo y luz | cielo marciano propio (caramelo de día, halo azul al atardecer, estrellas y Fobos de noche), sol con sombras y ciclo día/noche durante cada sol | reloj del juego |
| Ambiente | módulo de aterrizaje con baliza y foco, polvo en suspensión, anillos de evento (despliegue, mutación) | `SessionEvent.Cell` |

### Interfaz (TDD B.6)

Barra superior (sol, hora local, biomasa, poblaciones, pausa y tres velocidades, capa, guardar, cargar, nueva), panel de misión FIRST COLONY con progreso, inspector de celda con «Analizar región», laboratorio de diseño (organismo, biomasa, genes con deslizadores, «Experimentar» con gráfica de pronóstico y «Desplegar» haciendo clic en el terreno) y bitácora de eventos. Cámara orbital: botón derecho gira, rueda acerca, botón central o WASD desplaza, Q/E giran. Atajos: Espacio pausa, 0–4 capas, F5 guarda, F9 carga, Esc cancela el despliegue.

### Guardado

`SaveService` usa el mismo `SaveGameStore` y el mismo formato binario MRTS que el CLI (ADR-015), en `user://martis.db`. Cargar elige la partida más reciente y su última instantánea válida.

### Datos del juego

`DataRoot` busca `data/` subiendo desde el proyecto Godot, o usa `MARTIS_DATA_ROOT`. Una exportación tendrá que copiar `data/` junto al ejecutable (pendiente; no hay exportación en este hito).

## Consecuencias

- La recomendación del ROADMAP («textura generada desde datos de celdas») se cumple sobre un terreno 3D: el mapa sigue siendo una textura de 64×64 generada de los datos, proyectada sobre el relieve.
- MarsBaseline es uniforme (222 K, 146,5 W/m², 0,503 mSv/sol en todas las celdas). Las capas de temperatura y radiación se ven planas hasta que un escenario tenga gradientes; el relieve no los inventa.
- El laboratorio informa de la biomasa del organismo en la celda elegida (variantes existentes incluidas): un diseño igual a una variante que ya vive allí se fusiona con ella.

## Cómo se verifica

- `dotnet test MARTIS.sln`: `GameSessionTests` (partida completa de FIRST COLONY, laboratorio sin efectos, vista e inspector, despliegues inválidos, guardar y reanudar) y `GameBoundaryTests`.
- CI: `dotnet build src/Martis.Game/Martis.Game.csproj` en Linux.
- Local, con Godot 4.7.2 .NET:
  - `godot --headless --path src/Martis.Game -- --smoke` juega 30 soles, guarda, carga, compara y sale con 0.
  - `godot --path src/Martis.Game -- --shot=captura.png --hour=0.7` monta la demostración (5 colonias, 220 soles) y guarda una captura. Opciones: `--overlay=0..4`, `--distance`, `--yaw`, `--pitch`, `--sols`, `--seed`.
- Capturas de referencia (lavapipe, Vulkan por software): `docs/images/t08-atardecer.png`, `docs/images/t08-noche.png`, `docs/images/t08-capa-agua.png`.
