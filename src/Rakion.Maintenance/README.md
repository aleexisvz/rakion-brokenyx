# Rakion.Maintenance

Tareas programadas del servidor (fase 7, carril A), alojadas por `Rakion.App`. Reemplazan al `RankUpdate.exe` original.

## Ranking (7.1)

- `RankingScheduler` ejecuta `RankingJob` al arrancar y cada `Maintenance:RankingIntervalHours` horas (24 por defecto, `ProcessTime` del original). Los GM pueden lanzarlo con `/rankupdate`.
- Entran los personajes de cuentas conectadas en los últimos `RankingActiveMonths` meses (2, `RankMonth` del original), salvo `auth` 2, 10 y 0x34 (`RankUpdate.exe` 0x402202, **Observed**).
- Reglas de `RankingCalculator` (**Observed** en las consultas SQL de `RankUpdate.exe` y en 0x402300 / 0x402409):
  - puesto total por país según la EXP, con empates que comparten puesto (100, 100, 90 → 1, 1, 3);
  - puesto de clase entre todos los países, con la misma regla;
  - grado 26 a 15 por tramos fijos de EXP por debajo de 32 000;
  - desde 32 000, por país: 1 jugador grado 1, 4 grado 2, 16 grado 3, grados 4 a 13 hasta los cortes acumulados de 0,1 % (+1), 1, 3, 6, 10, 17, 27, 40, 56 y 76 % del resto, y grado 14 para los demás. Un tramo cuyo corte ya se alcanzó sigue recibiendo jugadores, igual que el bucle original.
- El resultado se guarda en `characters` (`rank_grade`, `total_rank`, `class_rank`), que viaja en la ficha de cada personaje al iniciar sesión, y en las tablas `ranking_total` y `ranking_class` para una futura web de ranking. El puesto anterior queda como `last_rank`.
- Pendiente: el ranking de clanes del original (`ClanRank`), cuando exista el sistema de clanes.

## Control de abusos (7.2)

Igual que el par World + `RankUpdate.exe` del original (**Observed**):

- El World guarda una fila por informe de ronda premiado en `match_point_log` (la `LogFieldGamePointList` del original): anfitrión, personaje, PowerUser, modo, rondas, victoria/derrota, EXP y oro. La EXP y el oro son los que informó el cliente, antes del bonus PowerUser y de los multiplicadores del servidor (**Inferred**), para que los umbrales signifiquen lo mismo que en el original. Los stages se guardan como empate (**Inferred**).
- Después del ranking, `AbuserCheckJob` suma por personaje los últimos `Maintenance:AbuserCheckDays` días (1) y guarda en `abuser_check` (la `LogFieldGameAbuserCheck` original) a quien llega a `AbuserWinSum` victorias (200), `AbuserExpSum` de EXP (6000) o `AbuserGoldSum` de oro (12 000); valores de `[ABUSER]` en el `Config.ini` original. Cada marcado sale como aviso en el log.
- No banea a nadie: los GM ven los últimos marcados con `/abusers` y deciden (`/ban`).
