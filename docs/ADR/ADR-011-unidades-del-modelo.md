# ADR-011 — Unidades del modelo

- **Estado:** Propuesto. MARTIS Lead lo pasa a Aceptado al fusionar este PR. Hasta entonces lo valida MARTIS Ciencia.
- **Cierra en parte:** [H-05](../TDD_HUECOS.md). [H-18](../TDD_HUECOS.md) queda parcial.
- **Fuente:** TDD v1.0 §2.2, §2.3, §2.5, §3.1, §3.2, §10.1, §10.2.

## Contexto

H-05: el paso de tiempo del TDD es 1 sol y el campo de edad se llamaba en días. Un sol no es un día terrestre. H-18: `ResourcePool` no tenía unidad ni matriz de composición. El agua aparecía a la vez como depósito y como estado ambiental sin una frontera comprobable.

La elección de unidad de un campo es `MODEL_ASSUMPTION`. La clasificación (`SCIENTIFIC_MEASURED`, `SCIENTIFIC_ESTIMATED`, `MODEL_ASSUMPTION`, `GAME_BALANCE`, `SPECULATIVE`), la fuente y el ámbito van **por valor** en el JSON del escenario (TDD §10.2), no pegadas al tipo. El cargador de T03 rechaza un valor sin `classification` o sin `source`, y avisa si una celda `Regional` lleva un valor `planetary_mean`. Eso se documenta aquí; no se implementa en T02. El ámbito principal sigue siendo el `Scope` de cada celda.

## Decisiones

### Tiempo

1 tick = 1 sol. `SecondsPerSol` = 88 775 s es un redondeo de modelo (`MODEL_ASSUMPTION`) del día solar medio de Marte. Fuente de la medición (`SCIENTIFIC_MEASURED`): D. R. Williams, NASA NSSDCA, Mars Fact Sheet, tabla «Orbital parameters», fila «Length of day (hrs)» 24,6597 h ≈ 88 774,92 s (https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html; consultado el 2026-11-16; la página no tiene fecha de actualización). Contraste: Allison & McEwen (2000), 88 775,244 s (NASA GISS Mars24, https://www.giss.nasa.gov/tools/mars24/help/notes.html). La diferencia es ≤ 0,25 s por sol (< 3·10⁻⁶). No es el periodo sideral (24,6229 h ≈ 88 642,4 s).

La cifra entera está solo en `MartianTime.SecondsPerSol`. En el código no existe `DeltaTimeDays`. La edad media de la población es `AgeDistributionMeanSols`, en soles. Cuando llegue la configuración, su paso también irá en soles.

### Depósitos y matriz A

Los nueve campos de `ResourcePool` son la masa del modelo en kg por celda, **después** de que el cargador convierta el dato de fuente. Hasta que se cierre H-07 no hay metros por celda: los datos de fuente se guardan intensivos (kg/m² o fracción másica) y el cargador de T03 los pasa a kg por celda con la geometría del escenario.

La matriz A guarda recuentos de átomos por especie. Esos recuentos son adimensionales. Las masas atómicas se citan de IUPAC/CIAAW (`SCIENTIFIC_MEASURED`); este ADR no copia cifras. La masa molar se deriva, no se guarda como constante:

`M_j = Σ_i A_ij · m_i`

`n_j = m_j / M_j`

`m_i` es la masa atómica del elemento i y `m_j` la masa del depósito j. La especie de cada depósito es `MODEL_ASSUMPTION` y queda pendiente de Ciencia para T04. Sin coeficientes inventados:

| Depósito | Especie pendiente | Fórmula posible |
| --- | --- | --- |
| CarbonDioxide | dióxido de carbono | CO₂ |
| Water | agua líquida accesible | H₂O |
| Oxygen | molecular o atómico | O₂ u O |
| Nitrogen | molecular o fijado | N₂ u otra especie |
| Phosphorus | elemento o fosfato | P o un fosfato |
| Sulfur | elemento o sulfato | S o un sulfato |
| Iron | elemento u óxido | Fe o un óxido |
| OrganicCarbon | carbono orgánico, especie sin fijar | pendiente |
| MineralCarbon | carbono mineral, especie sin fijar | pendiente |

`BiomassKg` tiene que declarar, junto a esa tabla, si es masa seca o húmeda. Todavía no lo hace (deuda R2-13).

El buffer de asignación usa kg por celda y no consume el depósito.

### Agua

| Compartimento | Dónde | Qué es | Base temporal |
| --- | --- | --- | --- |
| Líquido accesible | `ResourcePool.Water` | Lo único que la biología puede usar. kg por celda. | stock de la celda |
| Hielo | `EnvironmentState.WaterIce` | Reserva física. La biología no la consume. | stock de la celda |
| Vapor | `EnvironmentState.WaterVapor` | Reserva física. La biología no la consume. | stock de la celda |

No hay un segundo campo de líquido en el ambiente ni de hielo o vapor en el depósito. El agua adsorbida o hidratada queda fuera de v0.

`CellWaterBudget.RecordedTotalKg` no es un cuarto stock. El error es el valor absoluto de la diferencia entre `Water + WaterIce + WaterVapor` (en ese orden) y el total registrado. Pasa si el error es menor o igual que `MassBalanceAbsoluteToleranceKg + MassBalanceRelativeTolerance · |total|`. Las dos constantes son `MODEL_ASSUMPTION`. Lead fija la relativa en `1e-9`. La absoluta, en kg, es 0 y la cifra queda pendiente. Si la misma masa se anota en el líquido y en el hielo y el total no crece, el error supera esa cota y el validador emite **DOM-VAL-012**. Si el total se infla para absorber el doble conteo, esta regla no lo ve. La conservación de un tick al siguiente se comprueba en T04. Transferir agua conservando la masa es un proceso de T03/T04 y no está implementado.

### Fenotipo

`MaintenanceCost` es la fracción de `EnergyReserveJ` que se retira por sol, en [0, 1] (DOM-VAL-014). `ReproductionRate` es un factor de crecimiento por individuo y por sol, finito y ≥ 0, sin máximo declarado. Las dos escalas son `MODEL_ASSUMPTION` y pueden acabar en `GAME_BALANCE`. El resto de componentes del fenotipo no tiene unidad.

### Ambiente

La base temporal va por campo. La unidad elegida es `MODEL_ASSUMPTION`, salvo la temperatura y la presión, que heredan K y Pa del TDD §2.2. El intervalo [0, 1] de la porosidad es la definición de una fracción de volumen, no un supuesto.

| Campo | Unidad | Base temporal |
| --- | --- | --- |
| TemperatureKelvin | K, finito y > 0 si está asignado | Media del aire sobre los 88 775 s del sol. REMS mide aire y suelo, y son distintos. La media por sol esconde el ciclo diurno: simplificación `MODEL_ASSUMPTION`. |
| PressurePascal | Pa, finito y ≥ 0 si está asignado | Media sobre los 88 775 s del sol. |
| UvRadiation | W/m² en la banda 200–400 nm, mediodía local, superficie horizontal, como mide REMS UVS | No es una media del sol. La fluencia diaria en J/m² por sol está pendiente y no se suma tick a tick como si fuera esa media. |
| IonizingAbsorbedDoseGyPerSol | Gy por sol (Gy = J/kg), en tejido o agua, no en silicio. Dosis absorbida por rayos cósmicos galácticos en superficie. Los eventos SEP quedan fuera de v0. | Acumulada en el sol. No se suma ni se compara con el sievert. |
| IonizingEquivalentDoseSvPerSol | Sv por sol (Sv = Gy × Q, Q adimensional) | Acumulada en el sol. Usar el sievert para microorganismos es `MODEL_ASSUMPTION`: los factores de ponderación son de tejido humano. |
| SolarIrradiance | W/m² en superficie, no en el tope de la atmósfera | Media sobre los 88 775 s del sol. La energía del tick es `G · SecondsPerSol`, en J/m². La alternativa, no usada aquí, es guardar J/m² por sol. |
| WaterIce, WaterVapor | kg por celda | Stock. Ver la sección del agua. |
| SoilPorosity | fracción de volumen en [0, 1] | Definición del campo, no un supuesto de Marte. |
| PerchlorateMassKg | kg por celda, tras convertir una fracción másica | La especie (ClO₄⁻ o una sal de Ca/Mg) está pendiente. El cargador de T03 hace la conversión. |

Referencias de dosis, no valores de Gale y no constantes del código. Clasificación `SCIENTIFIC_ESTIMATED`:

- Hassler et al. 2014, Science 343:1244797, doi:10.1126/science.1244797. 0,210 ± 0,040 mGy/día ≈ 2,158·10⁻⁴ Gy/sol.
- Guo et al. 2021, doi:10.1007/s00159-021-00136-5. 0,49 ± 0,08 mSv/día ≈ 5,03·10⁻⁴ Sv/sol.

Pasar de «por día» a «por sol» multiplica por 88 775 / 86 400. Esa conversión es `MODEL_ASSUMPTION` porque supone que el día de RAD dura 24 h. Esas cifras no se copian al código; el escenario de T03 las traerá con fuente y clasificación.

Unset solo distingue temperatura y presión. El resto de campos ambientales nace en 0. El cargador de T03 tiene que escribirlos todos de forma explícita.

### Rejilla

El ejemplo del prototipo es 64×64 celdas. `GridIndex` no fija ese tamaño. H-07 sigue abierto: no hay metros por celda y no hay difusión. Por eso el kg por celda del modelo espera al cargador.

## Cómo se verifica

- `MartianTimeTests` exige que `88775` aparezca en un solo fichero de `src/`.
- Porosidad: 0 y 1 pasan; el `double` siguiente a 1 no.
- 0 K asignado se rechaza. Unset no es un cero asignado.
- DOM-VAL-012: 2 + 3 + 4 con total 9 pasa. 5 en líquido y 5 en hielo con total 5 falla. El valor cuya diferencia cabe en la tolerancia pasa; el `double` siguiente falla.
- Las dos dosis rechazan negativo, NaN e infinito por separado y no se suman.
- `MaintenanceCost` por encima de 1 lanza DOM-VAL-014. Un factor de reproducción mayor que 1 sigue siendo válido.

## Pendiente

Ciencia valida este ADR antes de que Lead lo acepte al fusionar. Siguen abiertos la especie de cada depósito, la masa seca o húmeda, la fluencia UV diaria, la tolerancia absoluta en kg y la cita numérica de IUPAC/CIAAW dentro del código de la matriz.
