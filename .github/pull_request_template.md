## Qué cambia

<!-- Resumen en dos o tres frases. Enlaza el hito, el pendiente (docs/ESTADO.md) o el hueco H-nn. -->

## Por qué

<!-- Sección del Documento Maestro v2.0 o ADR en la que se apoya. Si la decisión es nueva, añade la ADR. -->

## Cómo se ha probado

- [ ] `dotnet build MARTIS.sln -c Release` sin avisos
- [ ] `dotnet test MARTIS.sln -c Release`
- [ ] `godot --headless --path src/Martis.Game -- --smoke` (si toca el juego)
- [ ] `godot --headless --path src/Martis.Game -- --title-smoke` (si toca menús o audio)

<!-- Pega la salida relevante y, si cambia algo visible, una captura. -->

## Pendiente

<!-- Lo que queda fuera de este PR. -->

Revisión: [docs/PR_CHECKLIST.md](../docs/PR_CHECKLIST.md)
