# ADR-018 — Audio procedural: efectos, banda sonora generativa y viento

- **Estado:** Propuesto (pendiente de Dani), 2026-11-16.
- **Fuente:** petición de Dani (2026-11-16): «hay que hacer también una pantalla de inicio bonita animada, la banda sonora, las animaciones del videojuego y los sonidos in game». [ADR-000](ADR-000-reglas-de-arquitectura.md) R4, [ADR-003](ADR-003-prng.md), [ADR-017](ADR-017-juego-3d-godot.md).

## Contexto

El juego no tenía sonido. Hace falta música por estado (y por capítulo de la campaña «Génesis»), efectos para la interfaz y los eventos de la simulación, y volúmenes por canal. El audio tiene que ser original o CC0, con la fuente documentada.

## Decisiones

### Todo el audio se sintetiza desde código, salvo el piano

- `src/Martis.Audio` genera cada sonido al arrancar (efectos) o en streaming (música y viento) con código escrito para MARTIS. **La única excepción es el piano de cola**: Dani pidió el 2026-11-17 que sonara a piano de cola de verdad, y la síntesis no llegaba. Se usa un Steinway modelo B grabado de la *Versilian Community Sample Library* (VCSL, Versilian Studios LLC, **CC0 1.0**, https://github.com/sgossner/VCSL, carpeta `Chordophones/Zithers/Grand Piano, Steinway B/Sus`). CC0 no obliga a atribuir, pero la fuente queda aquí y en `PianoBank.cs`.
- `Martis.Audio` es una librería C# pura, **sin Godot ni referencias al motor** (`ProjectGraphTests`: `Martis.Audio` → nada; `Martis.Audio.Tests` → `Martis.Audio`; `Martis.Game` → Simulation, Persistence y Audio). Está en `MARTIS.sln` y se prueba con xUnit (`tests/Martis.Audio.Tests`).
- El audio usa su propio generador (`AudioRandom`, SplitMix64). Nunca consume números de los flujos de la simulación (ADR-003), y no usa `System.Random` (ADR-000 R4).

### Efectos (`SfxSynth`)

18 efectos a 44,1 kHz mono, con fundido de entrada y salida para que no hagan clic y masterizados uno a uno (ver «Mezcla y máster»):

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
- **Piano de cola grabado** (`PianoBank.cs`, `Piano.cs`): notas cada tono entero de La♯1 a Do7, con pedal, en dos capas de velocidad (suave y media). `tools/piano-bank/build_bank.py` las pasa a mono, las corta según el registro (9 s en los graves, 4,5 s en los agudos), les aplica un fundido de 1,5 s y las comprime en IMA ADPCM de 4 bits. El resultado es `Assets/piano.bank` (9,5 MB), incrustado en `Martis.Audio.dll` y descodificado una vez al primer uso (unos 37 MB de PCM de 16 bits en memoria). Cada nota usa la muestra más cercana (como mucho a un semitono) transpuesta con interpolación cúbica y mezcla las dos capas según la velocidad, así que tocar más suave también oscurece el timbre. Los apagadores cortan la nota en una décima de segundo cuando el pianista cambia el pedal con la armonía; por encima de Sol6 no hay apagadores, como en un piano real.
- **Cuerdas de conjunto** en lugar del pad de sintetizador: cada nota del acorde la tocan tres atriles (sierras sin aliasing por polyBLEP), con unos cents de diferencia, vibrato retardado de 4,6–5,8 Hz, deriva lenta de afinación, ataque de arco de 2,8 s y suelta de 3 s. Van muy por debajo del piano, como colchón. La sección pasa por las resonancias del cuerpo (aire a 280 Hz, madera a 1,1 kHz y puente a 2,6 kHz) y un paso bajo que sigue al estado. Las cuerdas respiran con el piano: sueltan al acabar cada frase.
- **Composición por frases:** de 2 a 4 compases. En cada compás, la mano izquierda toca según el estado: nota grave suelta, acorde quebrado o arpegio en corcheas. La mano derecha hace una melodía que camina por la escala, se apoya en notas del acorde en el primer tiempo y cierra la frase con una nota larga. Después viene un silencio de unos segundos, con el drone grave muy bajo y la cola de la sala.
- Cada decisión se toma sobre el reloj de muestras. Por eso la salida depende solo de la semilla y del momento en que cambia el estado, nunca del tamaño del buffer (test `TheChunkSizeDoesNotChangeTheMusic`).
- **Estados** (`MusicMood`): `Title`, `Day`, `Night`, `Thriving`, `Crisis`, `Victory`. Cada uno es un `MusicStyle` con tonalidad, modo, tempo, progresión, densidad de melodía, mano izquierda, silencio entre frases y nivel de pad, drone y latido. La noche es más escasa y lenta, el crecimiento más fluido y la crisis más grave, con latido. Un cambio de estado espera a que termine la frase en curso; si llega en un silencio, la siguiente frase empieza en 1,5 s como mucho. Los niveles se funden sin clics.
- **Tema de la pantalla de título** (`TitleTheme.cs`): pieza compuesta, no generada. Dani la pidió el 2026-11-17: lenta, profunda y relajante, en la línea de las bandas sonoras calmadas de los juegos de mundo abierto y de las de órgano del cine espacial, pero original. Va en re menor a 54 ppm y tiene seis secciones: intro de órgano solo; ostinato de piano en corcheas sobre i–VI–III–VII; melodía en notas largas; giro a sol menor que se abre a La mayor; clímax con órgano lleno y melodía en octavas; coda que baja hasta re. Se repite en bucle con 4 s de cola. El órgano es aditivo: principal de 8 pies siempre, y según la registración bordón de 16 pies, octava de 4 y docena de 2⅔. Cada tubo va doblado con uno o dos cents de diferencia y entra con un crescendo de 2,5 s. Un cambio de estado espera como mucho al final de la sección.
- **Capítulos:** `ch1`…`ch5` transponen la tonalidad y, en exploración (`Day`, `Thriving`), cambian el modo. Así cada capítulo de «Génesis» suena distinto.
- **Director** (`MusicDirector`), por prioridad: pantalla de inicio → `Title`; misión recién completada → `Victory` (24 s); fallo de la simulación o caída de biomasa de más del 10 % por sol → `Crisis`; biomasa ≥ 0,5 kg creciendo más de un 2 % por sol → `Thriving`; mundo sin vida → `Night`; si el sol dura menos de 30 s reales → `Day`; si no, día o noche según el sol. El juego solo cambia de estado si el nuevo se mantiene 3 s.

### Mezcla y máster (`Mastering.cs`)

Lo pidió Dani el 2026-11-17: instrumentación más orgánica y sonido bien masterizado. Las herramientas son estándar y tienen tests:

- **Medidor de sonoridad** `LoudnessMeter` según ITU-R BS.1770-4 / EBU R128: ponderación K, bloques de 400 ms con solape del 75 %, puerta absoluta a −70 LUFS y relativa a −10 LU. Calibrado con el seno de 997 Hz (0 dBFS en un canal = −3,01 LUFS).
- **Filtros** biquad RBJ, **compresor** estéreo enlazado con rodilla suave, **limitador** con lookahead de 5 ms (mínimo deslizante, ataque instantáneo, liberación exponencial y media de caja, así que nunca supera el techo) y **reverb FDN** de 8 líneas con matriz de Householder, reflexiones tempranas y pre-delay.
- **Música** (`MasterBus`): envío a una sala de 2,2 s con 22 ms de pre-delay; paso alto a 30 Hz; −1,5 dB a 280 Hz (barro); +1,5 dB por encima de 9 kHz (aire); compresión de pegamento 2:1 con ataque de 30 ms y liberación de 300 ms; un 15 % más de anchura M/S; trim por estado; limitador con techo a −1 dBFS, que deja margen para la codificación con pérdida. Cada estado está calibrado a **−18 LUFS integrados**, así que pasar de la noche a la crisis no cambia el volumen percibido (test `EveryMoodIsMasteredToTheSameLoudnessAndUnderTheCeiling`).
- **Efectos:** sala FDN de 1,1 s, paso alto a 60 Hz, compresión ligera 2:1, fundidos y normalización por sonoridad (máximo momentáneo) al objetivo de su categoría, pasando por el limitador a −1 dBFS: interfaz al pasar por encima −30 LUFS, clics −26, avisos −24, eventos −22 a −19, despliegue −18, victoria −17 y logo −16. La interfaz susurra y los hitos se oyen por encima de la música.

### Viento (`WindEngine`)

Dos bandas de ruido descorrelacionadas (estéreo) con ráfagas lentas y un silbido tenue. El nivel es bajo, por la atmósfera fina de Marte. Va por el bus Ambience.

### Integración en Godot

- Autoload `GameAudio` (`scripts/GameAudio.cs`): crea los buses **Music**, **Effects** y **Ambience** (todos envían a Master), renderiza los efectos a `AudioStreamWav` al arrancar y los toca con un grupo de 12 voces. La música y el viento van en streaming por `AudioStreamGenerator` (buffer de 1 s, bloques de 512 frames, como mucho 0,25 s por fotograma), en el hilo principal (ADR-000 R4).
- Todo `BaseButton` recibe sonido de hover y de clic al entrar en el árbol. Para excluir uno, se le pone el meta `silent`.
- Volúmenes lineales 0–1 por bus y silencio general en `user://audio.cfg` (`AudioSettings`, líneas `clave=valor`; si un valor no es válido, se conserva el anterior). Se convierten a dB con 20·log10 y el silencio está en −80 dB.
- Coste medido (Release, un núcleo): la música, con piano grabado, cuerdas y máster, cuesta entre 26 y 51 ms por segundo de audio según el estado, es decir, entre un 3 y un 5 % de un núcleo. La primera nota descodifica el banco (una sola vez).

## Consecuencias

- No hay assets binarios que mantener ni licencias que revisar. Para cambiar un sonido se edita el código, que está bajo test.
- La música no se puede «componer» en una DAW. Si un día hace falta música grabada, entraría como otro `MusicStyle` o como un bus aparte, con su licencia documentada aquí.
- Las pruebas comprueban propiedades medibles: determinismo, límites, ausencia de clics, brillo relativo y longitud. Que suene bien lo juzga Dani escuchando las muestras WAV exportadas.
