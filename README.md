# RTS Na Godote

Небольшая RTS на Godot 4.6 + C# с честной симуляцией, fog of war, строительством базы, добычей ресурсов, простым single-player loop и уже довольно большим слоем gameplay-логики поверх базового RTS-контроля.

Главная цель текущей кодовой базы: держать игру как набор понятных систем, где `Core` отвечает за симуляцию, а `Game` за presentation, input, HUD и эффекты.

## Технологии

- `Godot 4.6`
- `C# / .NET 8`
- один основной рантайм-сценарий: [Main.tscn](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Main.tscn)
- main assembly: [rts_na_godote.csproj](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/rts_na_godote.csproj)

## Как запустить

- открыть проект в Godot 4.6+
- стартовая сцена уже указана в [project.godot](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/project.godot)
- для проверки C#-части можно использовать `dotnet build`

## Что сейчас есть в игре

- две расы: `Alliance` и `Horde`
- игрок против enemy AI
- добыча `Gold` и `Lumber`
- здания: `TownHall`, `Farm`, `Barracks`, `Workshop`, `Tower`
- юниты: `Worker`, `Footman`, `Archer`, `Knight`, `Catapult`
- fog of war с remembered state для разведанных объектов
- строительство, производство юнитов, rally point
- attack-move, drag-select, minimap, pause menu
- under-attack ping и краткое раскрытие тумана войны вокруг атакующего
- enemy AI с профилями `Push` и `Harass`

## Структура проекта

### Core

`Core` содержит симуляцию и правила игры.

- [Core/Data/GameDefinitions.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameDefinitions.cs)
  Все основные статы юнитов, зданий и ресурсов.
- [Core/Data/GameConstants.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameConstants.cs)
  Общие константы карты, камеры, pathfinding, anti-stuck и worker-defense логики.
- [Core/Data/GameSettings.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameSettings.cs)
  Стартовые настройки матча, уровни сложности и AI profile.
- [Core/Simulation/GameSimulation.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/GameSimulation.cs)
  Сердце игры: update loop, команды юнитам, бой, экономика, здания, AI, победа/поражение.
- [Core/Simulation/Units/SimUnit.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Units/SimUnit.cs)
  Runtime-состояние юнита, его приказы и worker-specific поля.
- [Core/Simulation/Buildings/SimBuilding.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Buildings/SimBuilding.cs)
  Runtime-состояние зданий, стройка, очередь производства, атака башен.
- [Core/Simulation/Resources/SimResourceNode.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Resources/SimResourceNode.cs)
  Узлы ресурсов.
- [Core/Simulation/Economy/EconomySystem.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Economy/EconomySystem.cs)
  Деньги, дерево, food, cap.
- [Core/Simulation/Pathfinding/Pathfinder.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/Pathfinding/Pathfinder.cs)
  A* pathfinding c несколькими goal-кандидатами и штрафами на занятые тайлы.
- [Core/Simulation/World/MapGenerator.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/World/MapGenerator.cs)
  Генерация симметричной карты.

### Game

`Game` отвечает за то, как симуляция показывается игроку.

- [Game/Presentation/RtsGame.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Game/Presentation/RtsGame.cs)
  Главный orchestration-слой между input, camera, fog, selection, HUD и `GameSimulation`.
- [Game/Presentation/FogOfWar.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Game/Presentation/FogOfWar.cs)
  Локальное состояние видимости для игрока.
- [Game/UI/HudLayer.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Game/UI/HudLayer.cs)
  HUD, minimap, всплывающие сообщения, game over и pause menu.
- [AppRoot.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/AppRoot.cs)
  Главное меню и запуск матча.

## Runtime flow

1. `AppRoot` собирает стартовые настройки матча: `Race`, `Difficulty`, `AiProfile`, `Seed`.
2. `RtsGame.StartGame()` создает новую [GameSimulation.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/GameSimulation.cs).
3. Симуляция порождает стартовое состояние, карту, здания, ресурсы и юнитов.
4. Каждый `_Process()`:
   - тикает симуляция
   - обновляется fog of war
   - синхронизируются вьюхи
   - обновляется HUD
5. Все реальные игровые решения принимаются в `Core`, а presentation только подписывается на события симуляции.

## Основные gameplay-системы

### Карта и мир

- Карта генерируется симметрично.
- Базы игрока и AI зеркальны.
- Есть центральная lane-структура и дополнительные проходы.
- Лес и камень формируют choke points.
- Золотые шахты расставляются зеркально плюс есть contested центральные точки.

### Экономика

- Каждая сторона хранит `Gold`, `Lumber`, `Food`, `FoodCap`.
- `TownHall` и `Farm` поднимают cap.
- Worker добывает ресурс, потом несет его в `TownHall`.
- Производство юнитов и стройка идут через честное списание ресурсов без скрытых AI-бонусов.

### Бой

- Юниты и башни используют одну combat-модель через `ICombatTarget`.
- Есть melee, ranged и siege.
- `Catapult` имеет splash и bonus vs building.
- Общая логика выбора цели теперь заметно сильнее предпочитает вражеских юнитов постройкам.
- Если AI-юнит уже бьет здание, но в его боевой радиус входит вражеский юнит, цель может быть переоценена прямо в бою.
- Агро-механика сейчас намеренно ослаблена:
  - прямые приказы игрока важнее auto-retaliation
  - агр по удару в обычном виде остался только для `Idle`-юнитов
  - workers обслуживаются отдельной defensive логикой

### Pathfinding и движение

- Основа: A* по тайловой карте.
- Для цели может использоваться не один фиксированный тайл, а несколько кандидатов вокруг нее.
- В pathfinding есть `tieBreakerSeed`, чтобы одинаковые юниты не строили один и тот же маршрут.
- Есть мягкие tile-penalty вокруг других юнитов, чтобы пачка естественнее расходилась.
- Для боя, добычи, возврата ресурсов и стройки near-goal логика теперь старается разводить союзных юнитов по разным slot-кандидатам вокруг одной цели.
- Поверх pathfinding работает ослабленный anti-stuck:
  - срабатывает только если юнит реально почти не двигается
  - порог сейчас около `1 секунды`
  - дальше делается мягкий local-avoidance, попытка сменить near-goal slot и repath

### Worker safety logic

Для рабочих есть отдельная система выживания, не такая, как у боевых юнитов.

- Worker с активным ручным `Move` не срывается в бой.
- Worker на `Gather`, `Build`, `ReturnCargo` при ударе может:
  - временно драться под базой
  - либо flee к ближайшему `TownHall`
  - потом вернуть старый мирный приказ
- У этой системы есть safe radius и leash radius относительно `TownHall`.

### Fog of war

- У игрока есть два состояния тайла:
  - `visible`
  - `explored`
- Вражеские объекты, однажды увиденные, могут отображаться как remembered state, пока не будут опровергнуты повторной разведкой.
- Когда игрока атакуют, игра дополнительно раскрывает маленький радиус вокруг атакующего.

### HUD и input

- Большая часть UI построена на `CanvasLayer`.
- Пустые области HUD не должны блокировать мир.
- Drag-select продолжается даже если курсор зашел поверх HUD после начала рамки.
- Есть pause menu с `Resume`, `Settings` stub и `Main Menu`.

## Управление

### Мышь

- `LMB click`: выделение юнита или здания
- `LMB drag`: рамка выделения
- `Shift + LMB`: additive selection
- `RMB` по земле: move
- `RMB` по врагу: attack
- `RMB` по ресурсу: gather для workers
- `RMB` по недостроенному зданию: build для workers
- `RMB` по `TownHall` с грузом: return cargo
- колесо мыши: zoom
- minimap click: перемещение камеры

### Клавиатура

- `Esc`: cancel placement или pause
- `Space`: центр на `TownHall`
- `Q`: arm/disarm `Attack Move`
- `X`: stop
- `R`: restart match

### Hotkeys зданий и тренировки

- Постройка зданий рабочим:
  - `H`: `TownHall`
  - `F`: `Farm`
  - `B`: `Barracks`
  - `V`: `Workshop`
  - `T`: `Tower`
- Тренировка:
  - `E`: `Worker`
  - `F`: `Footman`
  - `G`: `Archer`
  - `K`: `Knight`
  - `C`: `Catapult`

## Enemy AI

Подробная документация по текущему enemy AI лежит отдельно:

- [docs/enemy-ai.md](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/docs/enemy-ai.md)

Коротко:

- AI честный: использует sight и memory, а не прямое чтение всей карты
- есть профили `Push` и `Harass`
- сложности меняют качество решений и тайминги, а не доход
- `Scout` теперь живет как отдельный non-combat recon-cycle и на `Normal/Hard` использует frontier игрокового вижена как navigation-only input
- scout не делает `peek` без обязательного `exit` и `fallback exit`, а route planner отбрасывает маршруты с длинной экспозицией внутри player vision
- после проверки одного frontier-сектора scout обязан уходить наружу и переключаться на другой сектор, если есть валидная альтернатива
- `Harass` теперь живет как отдельный raid-cycle: `Approach -> Raid -> Disengage -> Recover`
- в раннем `Harass` AI предпочитает worker line, mines и outer buildings вместо тупого захода в `TownHall`
- при явном преимуществе AI отзывает harass-группу и собирает полноценный push, а не пытается дожать базу маленьким рейдом
- в локальном бою AI теперь заметно охотнее переключается со зданий на подошедшие вражеские юниты
- AI умеет:
  - разведывать
  - собирать main army
  - regroup
  - defend
  - harass отдельным отрядом
  - заканчивать игру в `Finish`

## Текущее состояние и ограничения

- Проект уже хорошо подходит для gameplay-итераций.
- Основная архитектура понятная и расширяемая, но многое все еще завязано в одном большом `GameSimulation`.
- Enemy AI уже играбелен, но еще требует живой балансировки таймингов и порогов.
- Продвинутого микроконтроля вроде kite, split, spell logic и true threat map пока нет.
- Expansions, multi-base macro и глубокая tech progression пока не реализованы.

## Куда расширять дальше

- вынести AI из `GameSimulation` в отдельные компоненты/сервисы
- добавить debug HUD для текущего `AiState`, squad metrics и memory
- развить build-order / tech goals / expansions
- добавить сохранение и повтор воспроизводимых сценариев по seed
- покрыть симуляцию набором deterministic gameplay tests

## Git и GitHub

Если хочешь положить этот проект на GitHub с текущей машины, базовый поток такой:

1. В корне проекта выполнить `git init`.
2. Проверить файлы через `git status`.
3. Добавить всё в индекс: `git add .`.
4. Создать первый коммит: `git commit -m "Initial commit"`.
5. Создать пустой репозиторий на GitHub.
6. Привязать remote: `git remote add origin https://github.com/<username>/<repo>.git`.
7. Переименовать основную ветку при желании: `git branch -M main`.
8. Запушить: `git push -u origin main`.

Если удобнее через GitHub CLI и он установлен:

- `gh repo create <repo> --public --source . --remote origin --push`
- или `gh repo create <repo> --private --source . --remote origin --push`
