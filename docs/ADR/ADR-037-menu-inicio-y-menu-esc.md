# ADR-037 — Pantalla de inicio y menú Esc

- **Estado:** Propuesto, 2026-11-17.
- **Contexto:** la pantalla de inicio animada (A2, PR #19), el audio (A1, PR #18) y el menú de pausa (A3, PR #20) se quedaron en una rama aparte y no llegaron a la línea V1–M2. El juego arrancaba directamente en el menú de campaña y la barra superior tenía Guardar, Cargar, Nueva y Menú. Daniel pidió recuperar la pantalla de inicio «igual que antes» (continuar, nueva partida, cargar partida…), dejar la barra superior limpia y llevar esas acciones, con los modos F1–F4, a un menú que se abre con Esc.

## Decisiones

### Se integra la línea de audio y título

Esta rama fusiona `claude/project-thread-5n8taf-feel` (A1+A2+A3) sobre M2. Vuelven `Martis.Audio` (ADR-018), los autoloads `GameAudio` y `GameLaunch`, `scenes/Title.tscn` como escena principal, las animaciones del HUD y el menú de pausa. La toma de cámara de entrada de A3 no se conserva: el modo exploración ya sitúa la cámara detrás del rover.

### Menú de la pantalla de inicio

| Entrada | Qué hace |
|---|---|
| Continuar | Solo si hay partidas guardadas. Carga la más reciente (nombre, sol y fecha debajo). |
| Nueva partida | Expedición nueva (partida libre): rover, laboratorio y primera colonia. |
| Campaña | Abre los capítulos y misiones de «Génesis» (`CampaignMenu`). |
| Cargar partida | Lista de partidas guardadas (hasta 8, la más reciente primero) para elegir una. |
| Opciones | Volumen de música, efectos y ambiente. |
| Salir | Cierra el juego. |

`GameLaunch` lleva ahora `SaveId` además de `Mode` y `ChapterId`. `SaveService.Load(saveId, …)` carga una partida concreta con su misión y su expedición (V5); `LoadLatest` sigue igual. `SaveService.List()` da los resúmenes (`SaveSummary`) para los menús.

### Barra superior limpia y menú Esc

- La barra superior conserva solo la información y los controles de juego: sol, hora, biomasa, poblaciones, puntos, inóculo, velocidad, capa e Investigación.
- La pestaña de modos bajo la barra (`ModeBar`) desaparece. Las teclas F1–F4 y Tab siguen funcionando.
- Esc abre `PauseMenu`: detiene el tiempo y muestra una fila con EXPLORACIÓN F1, ESTRATÉGICO F2, LABORATORIO F3 y PLANETARIO F4 (el modo actual marcado), y debajo Continuar, Guardar partida (F5), Cargar partida (lista), Nueva partida, Campaña, Opciones de sonido, Volver al título y Salir.
- Esc dentro del menú vuelve atrás (de la lista de partidas al menú, del menú al juego). Con el menú abierto el mundo no recibe teclas, salvo F1–F4/Tab, que lo cierran y cambian de modo.
- Esc ya no devuelve del laboratorio o planetario al modo anterior; para eso están los botones de modo del menú y las teclas F.
- Esc en el menú de campaña vuelve a la partida si hay una en curso o, si no, a la pantalla de inicio.

### Pruebas

- `--smoke` comprueba también el menú Esc: para el tiempo, tiene los cuatro modos y las acciones, se cierra con Esc y la partida guardada aparece en la lista.
- `--title-smoke` comprueba el menú de inicio con y sin partidas guardadas (`--db=` elige la base de datos).
- Capturas: `--shot=x.png --esc-menu` y `--title-shot=x.png --title-load`.

## Consecuencias

- Al fusionar este PR también se integran A1–A3 (#18–#20); esos PRs quedan cubiertos por este.
- Quien añada una acción de partida nueva la pone en `PauseMenu`, no en la barra superior.
