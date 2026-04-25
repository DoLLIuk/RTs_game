# Architecture Map

Это короткая карта проекта для быстрого входа в код. Она не заменяет подробные документы, а помогает быстро понять, где теперь живут основные подсистемы и куда смотреть при изменениях.

## Main subsystems

- Simulation facade: [Core/Simulation/GameSimulation.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/GameSimulation.cs) и partial-файлы `GameSimulation.*.cs`. Здесь живут orchestration, core commands и unit/building tick loop.
- Pathfinding contracts: [Core/Simulation/Pathfinding/PathRequest.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/PathRequest.cs) и [Core/Simulation/Pathfinding/PathPlan.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/PathPlan.cs). Описывают запрос на маршрут и результат path planning.
- Path planner: [Core/Simulation/Pathfinding/Pathfinder.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/Pathfinder.cs) и [Core/Simulation/Pathfinding/UnitPathService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/UnitPathService.cs). Здесь живут A*, dynamic tile penalties, repath и stuck recovery.
- Local movement: [Core/Simulation/Pathfinding/LocalMovementService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/LocalMovementService.cs). Отвечает за локальный шаг, steering, collision checks и anti-overlap с реальным footprint зданий/ресурсов.
- Unit separation: [Core/Simulation/Pathfinding/UnitSeparationService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/UnitSeparationService.cs). Держит separation и head-on deadlock resolution.
- Static interaction geometry: [Core/Simulation/Pathfinding/StaticInteractionService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/StaticInteractionService.cs) и [Core/Simulation/Pathfinding/MovementTargetResolver.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/MovementTargetResolver.cs). Считают footprint-based interaction range и точки подхода к зданиям/ресурсам.
- Combat approach: [Core/Simulation/Pathfinding/CombatApproachService.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/CombatApproachService.cs). Строит melee/ranged approach slots вокруг боевых целей.
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
- Если меняешь A* / stuck recovery / dynamic penalties, смотри `UnitPathService` и `Pathfinder`.
- Если меняешь final approach к зданиям, добыче и сдаче ресурсов, смотри `StaticInteractionService` и `MovementTargetResolver`.
- Если меняешь локальное micro-движение и столкновения, смотри `LocalMovementService` и `UnitSeparationService`.

## Related docs

- Подробное описание текущего enemy AI: [docs/enemy-ai.md](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/docs/enemy-ai.md)
