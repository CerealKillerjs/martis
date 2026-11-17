# ADR-039 — El modo historia en una sola partida (M5)

- **Estado:** Propuesto, 2026-11-17.
- **Contexto:** Daniel quiere que la campaña sea lineal: una sola partida sobre un único terreno, de modo que se vea la progresión de los microbios a una civilización y se aprendan todos los conceptos por el camino. Hasta ahora, la campaña «Génesis» (H1–H8, PR #28) empezaba cada misión en un mapa nuevo de 64, 128 o 256 celdas. M3 y M4 (ADR-036, ADR-038) ya tienen el Marte real dividido en secciones que se abren por eras.

## Decisiones

### La siguiente misión continúa el mismo mundo

Sobre Marte real, «Siguiente misión» ya no crea un mapa: se captura la simulación y se reanuda con la misión siguiente y las reglas de su capítulo. Las reglas son el gradiente, las transiciones evolutivas y la red trófica de ese capítulo, al tamaño de la sección (256 celdas). El tiempo, las poblaciones y sus variantes siguen como estaban. La expedición (rover, conocimiento, muestras, estaciones, lugares y calefactores) pasa intacta. La economía de la campaña también se mantiene, porque los puntos de investigación son de toda la campaña.

Empezar el modo historia lleva siempre a la sección inicial (Isidis occidental), con el rover en el lugar de aterrizaje de Perseverance.

### Cada capítulo abre su era

Cada capítulo ya tiene su era (H8): los capítulos 1–2 son la Era I, los 3–4 la Era II y el 5 la Era III. Al pasar a un capítulo de una era nueva, se abren sus secciones del mosaico (M4), con un aviso que las nombra. La era alcanzada se guarda con el mapa del mundo.

### Guardar y cargar

Una partida guardada de una misión se carga con las reglas de su capítulo al tamaño del mapa guardado. Así, un capítulo 4 en la sección de 256 celdas no toma las reglas del primer capítulo de 256 celdas.

### Sin cambios en el terreno inventado

Con `--procedural`, las misiones siguen como antes: cada una en su mapa.

## Consecuencias

- Las misiones se siguen contando en la simulación de la sección inicial. Lo que pasa en las secciones vecinas todavía no cuenta para los objetivos.
- El gradiente de los capítulos 2–5 es sintético (más frío a un lado del mapa). Sobre el relieve real, lo correcto sería un entorno que salga de la altitud y la latitud reales. Queda como siguiente paso del anclaje de las celdas al planeta.
- Las misiones de un capítulo posterior encuentran el mundo que dejó el jugador: alguna puede ser más fácil o más difícil según cómo lo dejó. Es el efecto que se busca, pero habrá que equilibrarlas jugando.
