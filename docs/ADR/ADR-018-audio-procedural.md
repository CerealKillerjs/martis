# ADR-018 — Audio procedural: efectos, banda sonora generativa y viento

- **Estado:** Propuesto (pendiente de Dani), 2026-11-16.
- **Fuente:** petición de Dani (2026-11-16): «hay que hacer también una pantalla de inicio bonita animada, la banda sonora, las animaciones del videojuego y los sonidos in game». [ADR-000](ADR-000-reglas-de-arquitectura.md) R4, [ADR-003](ADR-003-prng.md), [ADR-017](ADR-017-juego-3d-godot.md).

## Contexto

El juego no tenía sonido. Hace falta música por estado (y por capítulo de la campaña «Génesis»), efectos para la interfaz y los eventos de la simulación, y volúmenes por canal. El audio tiene que ser original o CC0, con la fuente documentada.

## Decisiones

### Todo el audio se sintetiza desde código

- **No hay ficheros de audio en el repositorio.** `src/Martis.Audio` genera cada sonido al arrancar (efectos) o en streaming (música y viento). Fuente de todo el audio: el código de este repositorio, escrito para MARTIS; no hay muestras, bancos ni loops de terceros, así que no hay licencias que atribuir.
- `Martis.Audio` es una librería C# pura, **sin Godot ni referencias al motor** (`ProjectGraphTests`: `Martis.Audio` → nada; `Martis.Audio.Tests` → `Martis.Audio`; `Martis.Game` → Simulation, Persistence y Audio). Está en `MARTIS.sln` y se prueba con xUnit (`tests/Martis.Audio.Tests`).
- El audio usa su propio generador (`AudioRandom`, SplitMix64). Nunca consume números de los flujos de la simulación (ADR-003), y no usa `System.Random` (ADR-000 R4).

### Efectos (`SfxSynth`)

18 efectos a 44,1 kHz mono, normalizados a un pico por efecto y con fundido de entrada y salida para que no hagan clic:

| Efecto | Cuándo | Síntesis |
| --- | --- | --- |
| `UiHover`, `UiClick`, `UiBack` | pasar por encima o pulsar cualquier botón; desactivar un conmutador | blips senoidales cortos |
| `Pause`, `Resume` | Espacio | glissando FM descendente o ascendente |
| `Toast`, `Error` | avisos normales o de error | campana FM de dos notas; pulso cuadrado filtrado |
| `Deploy` | evento `deploy` | silbido de cápsula (ruido con paso banda descendente), golpe grave y burbujeo de esporas en Lidio |
| `Mutation` | evento `mutation` | glissando FM ascendente con destellos de campana |
| `Extinction` | evento `extinction` | caída grave de dos senos con retumbo de ruido |
| `Mission` | evento `mission` | arpegio mayor de campanas |
| `Victory` | misión completada | acorde de pad en Fa lidio, arpegio ascendente y brillo |
| `Save`, `Load` | F5, F9 | trinos de dos tonos (subida o bajada) |
| `Analyze`, `Experiment` | analizar región, laboratorio | barrido con trémolo; burbujas ascendentes |
| `Transition` | cambios de pantalla | ráfaga de aire filtrada |
| `TitleSting` | aparición del logo | impacto grave, caída de sub y pad ascendente |

Cada refresco del juego suena como mucho un efecto de evento, por prioridad: misión, extinción, fallo, mutación, despliegue, experimento.

### Banda sonora generativa (`MusicEngine`)

- **Piano solo, tranquilo y con silencios**, en la línea de las bandas sonoras calmadas de los juegos de mundo abierto. Lo pidió Dani el 2026-11-17: «menos campanitas y más piano». La música es original: frases generadas al momento, sin citar ninguna obra.
- **Piano aditivo:** ocho parciales por nota, ligeramente estirados (inharmonicidad de cuerda). Cada parcial decae a su ritmo: los agudos se apagan antes y las notas graves duran más. Tiene el golpe del macillo, que se funde en la cola, y dos cuerdas desafinadas en los tres primeros parciales para el batido natural. Un paso bajo suave hace de fieltro.
- **Composición por frases:** de 2 a 4 compases. En cada compás, la mano izquierda toca según el estado: nota grave suelta, acorde quebrado o arpegio en corcheas. La mano derecha hace una melodía que camina por la escala, se apoya en notas del acorde en el primer tiempo y cierra la frase con una nota larga. Después viene un silencio de unos segundos, con un pad cálido y un drone grave muy bajos y una reverb amplia.
- Cada decisión se toma sobre el reloj de muestras. Por eso la salida depende solo de la semilla y del momento en que cambia el estado, nunca del tamaño del buffer (test `TheChunkSizeDoesNotChangeTheMusic`).
- **Estados** (`MusicMood`): `Title`, `Day`, `Night`, `Thriving`, `Crisis`, `Victory`. Cada uno es un `MusicStyle` con tonalidad, modo, tempo, progresión, densidad de melodía, mano izquierda, silencio entre frases y nivel de pad, drone y latido. La noche es más escasa y lenta, el crecimiento más fluido y la crisis más grave, con latido. Un cambio de estado espera a que termine la frase en curso; si llega en un silencio, la siguiente frase empieza en 1,5 s como mucho. Los niveles se funden sin clics.
- **Capítulos:** `ch1`…`ch5` transponen la tonalidad y, en exploración (`Day`, `Thriving`), cambian el modo. Así cada capítulo de «Génesis» suena distinto.
- **Director** (`MusicDirector`), por prioridad: pantalla de inicio → `Title`; misión recién completada → `Victory` (24 s); fallo de la simulación o caída de biomasa de más del 10 % por sol → `Crisis`; biomasa ≥ 0,5 kg creciendo más de un 2 % por sol → `Thriving`; mundo sin vida → `Night`; si el sol dura menos de 30 s reales → `Day`; si no, día o noche según el sol. El juego solo cambia de estado si el nuevo se mantiene 3 s.

### Viento (`WindEngine`)

Dos bandas de ruido descorrelacionadas (estéreo) con ráfagas lentas y un silbido tenue. El nivel es bajo, por la atmósfera fina de Marte. Va por el bus Ambience.

### Integración en Godot

- Autoload `GameAudio` (`scripts/GameAudio.cs`): crea los buses **Music**, **Effects** y **Ambience** (todos envían a Master), renderiza los efectos a `AudioStreamWav` al arrancar y los toca con un grupo de 12 voces. La música y el viento van en streaming por `AudioStreamGenerator` (buffer de 1 s, bloques de 512 frames, como mucho 0,25 s por fotograma), en el hilo principal (ADR-000 R4).
- Todo `BaseButton` recibe sonido de hover y de clic al entrar en el árbol. Para excluir uno, se le pone el meta `silent`.
- Volúmenes lineales 0–1 por bus y silencio general en `user://audio.cfg` (`AudioSettings`, líneas `clave=valor`; si un valor no es válido, se conserva el anterior). Se convierten a dB con 20·log10 y el silencio está en −80 dB.
- Coste medido (Release, un núcleo): la música cuesta entre 38 y 89 ms por segundo de audio según el estado, es decir, entre un 4 y un 9 % de un núcleo.

## Consecuencias

- No hay assets binarios que mantener ni licencias que revisar. Para cambiar un sonido se edita el código, que está bajo test.
- La música no se puede «componer» en una DAW. Si un día hace falta música grabada, entraría como otro `MusicStyle` o como un bus aparte, con su licencia documentada aquí.
- Las pruebas comprueban propiedades medibles: determinismo, límites, ausencia de clics, brillo relativo y longitud. Que suene bien lo juzga Dani escuchando las muestras WAV exportadas.
