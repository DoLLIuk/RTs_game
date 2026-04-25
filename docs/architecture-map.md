# Architecture Map

Это короткая карта проекта для быстрого входа в код. Она не заменяет подробные документы, а помогает быстро понять, где теперь живут основные подсистемы и куда смотреть при изменениях.

## Main subsystems

- Simulation facade: [Core/Simulation/GameSimulation.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/GameSimulation.cs) и partial-файлы `GameSimulation.*.cs`. Здесь живут orchestration, core commands и unit/building tick loop.
- AI knowledge: [Core/Simulation/AI/AiKnowledgeService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/AI/AiKnowledgeService.cs). Отвечает за enemy memory, freshness и knowledge queries.
- Scout: [Core/Simulation/AI/Scout/ScoutSystem.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/AI/Scout/ScoutSystem.cs). Управляет scout mission, recall-to-assembly и scout-specific threat logic.
- Army planning: [Core/Simulation/AI/AiArmyManager.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/AI/AiArmyManager.cs). Делит армию на `mainArmy` и `harassSquad`, считает squad metrics и выбирает strategic state.
- Harass: [Core/Simulation/AI/Harass/HarassMissionService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/AI/Harass/HarassMissionService.cs). Держит harass lifecycle, objectives, disengage/recover и harass micro.
- Economy planning: [Core/Simulation/AI/AiEconomyPlanner.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/AI/AiEconomyPlanner.cs). Отвечает за worker assignment, build-production decisions и AI rally planning.

## If you change X

- Если меняешь AI state transitions или squad split, смотри `AiArmyManager` и `RunAi()` в `GameSimulation`.
- Если меняешь разведку, смотри `ScoutSystem`, `ScoutContext` и scout wiring в `GameSimulation.Scout.cs`.
- Если меняешь harass, смотри `HarassMissionService` и его context.
- Если меняешь производство AI или worker automation, смотри `AiEconomyPlanner`.
- Если меняешь реальные команды юнитов или path/order semantics, смотри `GameSimulation` command API (`IssueMove`, `IssueAttackMove`, `IssueAttack`, `IssueGather`, `IssueBuild`).

## Related docs

- Подробное описание текущего enemy AI: [docs/enemy-ai.md](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/docs/enemy-ai.md)
