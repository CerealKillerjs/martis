# Estado del proyecto

Actualizado el 2026-11-17, cuando se fusionaron en `main` todos los hitos que estaban en ramas apiladas (PRs #8 a #40).

## Dónde estamos

Según las fases del [Documento Maestro v2.0](maestro/MARTIS_Documento_Maestro_v2.0.docx) (§31 a §34):

| Fase | Estado |
| --- | --- |
| A. Fundamentos: motor headless, contratos, datos, RNG, guardado | hecha (T01–T07, T10) |
| B. Biología: metabolismo, rasgos, reproducción, evolución | hecha (T04–T06) |
| C. Ecología: interacción, ciclos, diversidad | hecha (T09 y capítulos H4–H7) |
| D. Vertical slice: rover, mundo 3D, laboratorio, UI, modo estratégico, primera colonia, guardar y cargar | hecha (V1–V5), ahora sobre el Marte real (M1–M5) |
| E. Terraformación regional | empezada: calefactores regionales (E1) |
| F. Contenido: misiones, regiones, progresión, narrativa | campaña «Génesis» de 5 capítulos y 18 misiones; modo historia en una sola partida |
| G. Sistemas avanzados: multicelularidad, inteligencia | base jugable en los capítulos 3–5 (H5–H7) |
| H. Multiplanetario | solo la base de datos por planeta (`PlanetDefinition`, ARC-003) |
| I. Interestelar | no empezada |

## Hitos fusionados

| Hito | PR | Qué aporta | ADR |
| --- | --- | --- | --- |
| T01–T03b | #1–#8 | solución .NET 10, dominio, motor de ticks, PRNG, escenario MarsBaseline | 000–011 |
| S1-31..33 | #9 | snapshot completo y CLI `simulate` | 004 |
| T04–T07 | #10–#13 | metabolismo, poblaciones, evolución, guardado SQLite | 012–015 |
| T09, T08, T10 | #14–#16 | ecosistema y FIRST COLONY, juego 3D en Godot, benchmarks | 016–017 |
| A1–A3 | #18–#20 | audio (piano VCSL, música por estados de ánimo), pantalla de inicio, animaciones | 018 |
| H1–H8 | #17, #21–#26, #28 | modo historia «Génesis», economía e investigación, mapas grandes, capítulos 2–5, guía y códice | 019–025, 028 |
| J1 | #27 | rover en tercera persona | 026 |
| V1–V5 | #29–#33 | modos sobre un estado, conocimiento y escáner, muestras y laboratorio, robots y estaciones, valle y guardado completo | 027, 029–032 |
| E1 | #34 | calentamiento regional con calefactores | 033 |
| M1–M5 | #35–#37, #39, #40 | relieve real de Jezero, escala real por trozos, secciones de Marte, mundo por secciones, historia en una sola partida | 034–036, 038–039 |
| Menús | #38 | pantalla de inicio con partidas guardadas y menú Esc con los modos F1–F4 | 037 |

Las ramas originales de cada hito siguen en GitHub como referencia. El trabajo nuevo sale de `main`.

## Pendientes conocidos

Están ordenados de más a menos urgente. Los que necesitan decisión están también en [TDD_HUECOS.md](TDD_HUECOS.md).

1. **Los objetivos de misión solo cuentan en la sección inicial** (H-35). Con el mundo por secciones (M4/M5), lo que el jugador hace en una sección vecina no cuenta para la misión en curso. Hace falta un seguimiento de misión agregado sobre todas las secciones abiertas ([ADR-039](ADR/ADR-039-modo-historia-una-partida.md)).
2. **El clima por celda usa un gradiente inventado** (H-36). Los capítulos 2–5 enfrían un lado del mapa con un gradiente `GAME_BALANCE` (`EnvironmentGradient`) en lugar de derivar la temperatura de la altitud y la latitud reales del relieve. El siguiente paso es un entorno anclado al planeta (Documento Maestro §19.1, ARC-003).
3. **Rendimiento con muchas poblaciones** (H-29, H-34). Las mutaciones llevan la carga de prueba de 10.000 a unas 80.000 poblaciones, y en 256×256 el tick ronda los 74 ms ([BENCHMARK.md](BENCHMARK.md)). Ponerse al día al entrar en una sección también tarda más cuanta más vida tiene.
4. **Equilibrio de misiones y economía** (H-32, H-33). Los umbrales y costes son propuestas de juego que hay que ajustar jugando. En el modo historia, una misión posterior encuentra el mundo tal como lo dejó el jugador.
5. **Test `GlobalJsonPinsTheNet10Sdk` en entornos sin el SDK 10.0.404** (H-37). En la nube del asistente solo hay SDK 10.0.1xx: allí se edita `global.json` en local para compilar y ese test falla. En la CI y con el SDK correcto pasa. No hay que subir nunca ese cambio de `global.json`.
6. **Sin licencia de código** (H-23) ni exportación del juego (plantillas de exportación de Godot).
7. La mayoría de ADR desde la 012 están en estado *Propuesto*: conviene revisarlas y pasarlas a *Aceptado*.

## Siguientes pasos sugeridos

Según el orden de producción del Documento Maestro (§34), la fase E sigue abierta:

- **Agua y hielo, y cúpulas**: estados del agua por celda y estructuras que cambian el ambiente local.
- **Energía solar con polvo**: generación ligada a la irradiancia y a las tormentas de polvo, compartida con estaciones y calefactores (`FacilityNetwork`).
- **Métricas regionales y una misión de terraformación**. La terraformación no es una barra de progreso: son métricas físicas por región.
- **Entorno real por celda** (pendiente 2), que es lo que más mejora la coherencia del mundo abierto.
- **Misiones sobre todas las secciones** (pendiente 1).

## Cómo retomar el trabajo

1. Clona el repositorio y sigue el [README](../README.md) (SDK 10.0.404 y Godot 4.7.2 .NET).
2. Comprueba que `dotnet test` y `-- --smoke` pasan en tu máquina.
3. Escoge un pendiente o un paso de la lista anterior, crea tu rama desde `main` y sigue [CONTRIBUTING.md](../CONTRIBUTING.md).
