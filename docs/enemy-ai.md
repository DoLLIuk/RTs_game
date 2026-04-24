# Enemy AI

Этот документ описывает текущее состояние enemy AI в проекте. Он ориентирован не на «идеальный будущий AI», а на то, как AI реально работает сейчас в коде.

Главная точка входа:

- [Core/Simulation/GameSimulation.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/GameSimulation.cs)

Связанные настройки:

- [Core/Data/GameSettings.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameSettings.cs)
- [AppRoot.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/AppRoot.cs)
- [Core/Data/GameConstants.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameConstants.cs)

## Цели текущей версии

- сделать AI честным по информации
- различать сложности качеством решений, а не income cheat
- поддерживать два стиля игры:
  - `Push`
  - `Harass`
- дать AI базовую RTS-структуру:
  - экономика
  - разведка
  - состояние стратегии
  - управление main army
  - простое squad micro

## Публичные настройки

### AiProfile

`AiProfile` выбирается в главном меню и прокидывается в `GameInit`.

- `Push`
  AI почти всегда нацелен на рост main army и тайминговый пуш.
- `Harass`
  AI старается отделить небольшой мобильный отряд и давить workers / outer targets, не уводя всю армию.

### Difficulty

Сложность задается не бонусами к ресурсам, а decision-параметрами.

Текущие профили:

| Difficulty | AiDelayMs | TargetWorkers | ScoutDelayMs | ScoutMaxExposureMs | ScoutReentryDelayMs | PushMinPower | HarassMinPower | AttackAdvantageRatio | RetreatRatio | RegroupDurationMs | DefendRadiusTiles |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Easy | 1200 | 7 | 7000 | 1350 | 1650 | 9 | 7 | 1.32 | 0.72 | 5200 | 11 |
| Normal | 750 | 10 | 4200 | 900 | 900 | 11 | 8 | 1.08 | 0.58 | 4000 | 12 |
| Hard | 520 | 13 | 2500 | 650 | 550 | 12 | 9 | 0.92 | 0.46 | 3000 | 13 |

Что это значит практически:

- `AiDelayMs`
  Как часто AI тикает свою high-level логику.
- `TargetWorkers`
  Сколько workers AI хочет держать в экономике.
- `ScoutDelayMs`
  Когда AI начинает активно искать базу игрока.
- `ScoutMaxExposureMs`
  Сколько scout может прожить внутри опасного окна до обязательного disengage.
- `ScoutReentryDelayMs`
  Минимальная пауза перед повторным заходом с другого сектора.
- `PushMinPower`
  Минимальная сила main army для полноценного push.
- `HarassMinPower`
  Минимальная сила harass squad.
- `AttackAdvantageRatio`
  Насколько уверенно AI должен превосходить известную силу врага, чтобы пушить.
- `RetreatRatio`
  При каком соотношении AI должен отступать.
- `RegroupDurationMs`
  Минимальная пауза на сбор армии.
- `DefendRadiusTiles`
  Радиус локальной опасности вокруг своей базы.

## Высокоуровневая архитектура

AI логически собран из трех слоев.

### 1. Knowledge model

AI не читает всю карту напрямую для принятия стратегических решений.

Он собирает внутреннюю память:

- `AiMemory.Units`
- `AiMemory.Buildings`
- `AiMemory.LastKnownPlayerBase`
- `AiMemory.LastKnownPlayerBaseTile`
- `AiMemory.LastContactMs`

Это memory-модель «last known alive until disproven».

Принцип:

- если AI видит объект игрока, он обновляет запись
- если AI не видит объект, но снова видит его старую позицию пустой, запись может быть удалена
- town hall игрока обновляет `LastKnownPlayerBase`

Видимость считается только через реальный sight AI-юнитов и AI-зданий:

- `CanAiSeePosition(...)`

То есть AI знает только то, что сам мог увидеть.

### 2. Strategic brain

Главное состояние AI хранится в `_aiState`.

Текущие состояния:

- `Open`
- `Scout`
- `Boom`
- `Defend`
- `Regroup`
- `Push`
- `Harass`
- `Finish`

Переходы вычисляются в:

- `DetermineAiState(...)`

### 3. Army manager

AI не раздает приказы наугад каждому юниту во все стороны. Сначала он:

- делит армию на `mainArmy` и `harassSquad`
- считает метрики squads
- выбирает anchor/target
- строит формацию
- раздает приказы стройными рядами

Основные методы:

- `BuildAiSquads(...)`
- `CalculateSquadMetrics(...)`
- `ExecuteAiState(...)`
- `CommandSquad(...)`
- `CommandFormationRow(...)`
- `ApplyAiMicro(...)`

## Knowledge model подробно

### Что именно помнит AI

По юнитам:

- `Id`
- `Kind`
- `Position`
- `Power`
- `LastSeenMs`

По зданиям:

- `Id`
- `Kind`
- `Position`
- `CenterTile`
- `MaxHp`
- `LastSeenMs`

### Как обновляется память

1. AI проходит по всем живым объектам игрока.
2. Проверяет, попадает ли объект в sight любой AI-единицы или AI-здания.
3. Если да, записывает/обновляет запись в memory.
4. Если контакт был, обновляет `LastContactMs`.

### Как чистится память

Память не вечная и не магическая.

`CleanupAiMemory(...)` удаляет записи, если:

- реальный объект уже не существует
- AI снова видит это место
- и там действительно больше ничего нет

Для базы игрока есть дополнительный нюанс:

- если был удален remembered `TownHall`, то `LastKnownPlayerBase` очищается

### Fresh memory

Не вся память одинаково ценна. Для ряда решений AI использует только «свежие» записи:

- `IsFreshEnemyMemory(lastSeenMs)`

Сейчас память считается свежей около `28000 ms`.

Это влияет на:

- оценку силы врага
- выбор push target
- определение наличия town hall
- оценку давления рядом с базой AI

## Состояния AI

### Open

Начальное состояние матча.

Поведение:

- запускает экономику
- добирает workers
- стремится построить первый `Barracks`
- не отправляет main army в глубокую агрессию
- обычно держит армию около staging point

### Scout

Включается, когда база игрока еще не подтверждена и вышло время `ScoutDelayMs`.

Поведение:

- выбирает разведчика по `speed + sight + survivability`, с приоритетом `Knight -> Archer -> Footman`
- если армии нет, может использовать один закрепленный worker fallback, но только когда есть безопасный вход/выход
- во время `Scout` юнит находится в non-combat lock и не атакует вообще
- на `Normal` и `Hard` scout использует текущую видимость игрока только как navigation input:
  - видит frontier между `visible / non-visible`
  - планирует `entry`, `peek`, `exit`, `fallback exit`
  - отклоняет сектор, если путь к нему или обратно слишком долго проходит через видимую зону
- живет как recon-cycle:
  - `ApproachEdge`
  - `Peek`
  - `BreakContact`
  - `Reposition`
  - `ReEnter`
- кратко входит в vision edge около worker line / outer building / tower perimeter / army edge
- заранее планирует `entry`, `planned exit` и `fallback exit`
- при прямой угрозе или по истечении exposure window немедленно ломает контакт
- после завершенного `peek` обязан сменить frontier-сектор, если есть валидный альтернативный
- остальная армия не коммитится в бой, а ждет ближе к staging area

Важный нюанс:

- suspected base по умолчанию берется из `Layout.PlayerBase`, то есть AI знает стартовую позицию противника как часть симметричного сценария карты
- но реальная стратегическая память о базе игрока начинает жить отдельно после actual scouting

### Boom

Промежуточное macro-состояние.

Поведение:

- добирает workers до `TargetWorkers`
- следит за food cap
- строит tech/buildings
- не пушит без нужной силы

### Defend

Включается, если есть `KnownEnemyPressureNear(...)` рядом с `TownHall`.

Поведение:

- main army получает приоритет на возврат к базе
- harass squad тоже подтягивается назад
- при давлении может построиться `Tower`, если поблизости ее еще нет

Это состояние имеет наивысший практический приоритет среди обычных mid-game состояний.

### Regroup

Состояние сбора армии.

Поведение:

- AI тянет main army в staging point
- harass squad тоже не уходит далеко
- армия выстраивается в formation rows
- есть минимальная пауза по `RegroupDurationMs`, чтобы AI не дергался между push/retreat слишком часто

### Push

Основной лобовой режим атаки.

Включается, если:

- есть frontline
- достаточно общей силы
- known enemy power не требует retreat
- выполняется `ShouldPush(...)`

Поведение:

- main army идет в сторону `FindPushTargetPosition(...)`
- используется attack-move
- harass squad, если она существует, не коммитится рядом, а чаще стоит ближе к stage point
- если AI знает не только `TownHall`, push старается сначала выбрать более внешний или более военный building target, а не сразу центр базы
- если надежной building-цели нет, AI может идти не прямо в `TownHall`, а в точку подхода перед базой, чтобы бой начинался естественнее

### Harass

Специальный режим только для профиля `Harass`.

Включается, если:

- у AI профиль `Harass`
- есть `harassSquad`
- известна база игрока
- прошло достаточно времени с прошлого harass-приказа
- squad power выше `HarassMinPower`
- main army не развалена

Поведение:

- main army остается ближе к staging point
- harass squad теперь живет как отдельная mission-state машина:
  - `Approach`
  - `Raid`
  - `Disengage`
  - `Recover`
- в early/mid game `Harass` пытается бить экономику:
  - worker line
  - gold line
  - outer buildings
- `TownHall` не считается нормальной ранней harass-целью, пока есть более логичные экономические или outer targets
- если рейд уже дал выгоду, но локальная драка ломается не в пользу AI, harass-отряд уходит в recover вместо тупого залипания под базой
- если общий перевес AI уже достаточно большой, harass не эскалирует сам в мини-push, а отзывается под общий `Regroup/Push`

### Finish

Режим добивания.

Включается, когда:

- у игрока уже не видно town hall в актуальной memory
- или AI имеет явное силовое преимущество

Поведение:

- меньше пауз
- и main army, и harass squad могут идти на общий финальный target
- задача не «прощупывать карту», а дожимать

## Как AI оценивает силу

### Main metrics

Для каждого squad считаются:

- `Center`
- `Power`
- `SlowestSpeed`
- `FrontlineCount`
- `BacklineCount`
- `SiegeCount`
- `Count`

Сила юнита считается как:

- `unit.Score * hpRatio`

То есть побитая армия автоматически считается слабее.

### Known enemy power

`EstimateKnownEnemyPower()` суммирует:

- свежие remembered enemy units
- свежие remembered enemy buildings с разным весом

Приблизительные building scores:

- `Tower` примерно `2.6`
- `TownHall` примерно `2.2`
- остальные tech/buildings примерно `1.2`

Это не идеальная модель, но она дешевая и достаточно читаемая для текущей версии.

## Экономика и производство AI

Главный метод:

- `MaintainAiEconomy(...)`

Что он делает:

- следит за food cap и заказывает `Farm`
- заказывает workers до `TargetWorkers`
- строит `Barracks`
- позже строит `Workshop`
- строит `Tower`, если на базе есть давление
- настраивает rally points production buildings в сторону текущего фронта

### Barracks production

Решение принимает `PickBarracksUnit(...)`.

Текущая логика:

- пытается держать баланс `Footman` / `Archer`
- при наличии `Workshop` начинает добавлять `Knight`
- в профиле `Harass` раньше допускает набор мобильных `Knight`

### Siege production

Решение принимает `ShouldBuildSiege(...)`.

AI начинает считать siege полезным, если:

- уже видел важные здания игрока
- или вышел на достаточную силу
- или еще не имеет ни одной siege-единицы к моменту нужного power-level

## Squad splitting

### Push profile

Если профиль `Push`:

- вся армия идет в `mainArmy`
- `harassSquad` не выделяется

### Harass profile

Если профиль `Harass`:

- при достаточном размере армии и наличии known player base AI может выделять динамический `harassSquad`
- `Catapult` никогда не идет в harass
- приоритет кандидатов:
  - `Knight`
  - `Archer`
  - `Footman`
- AI следит, чтобы после выделения у него не развалилась main army:
  - в основе должны остаться frontline units
  - main army должна сохранять большую часть общей силы

## Formation и командование

`CommandSquad(...)` делит squad на:

- frontline
- backline
- siege

Дальше ряды ставятся относительно anchor и направления на target.

Текущее построение:

- frontline впереди
- ranged позади
- siege еще дальше сзади

Важно:

- AI не просто кидает всем один `Move` в одну точку
- он строит formation rows через `CommandFormationRow(...)`
- это помогает армии подходить более внятно и снижает traffic problems

## Выбор целей

### На стратегическом уровне

`Push` и `Harass` теперь разведены сильнее.

`FindPushTargetPosition(...)` предпочитает remembered buildings с весами:

- `Tower`
- `Workshop`
- `Barracks`
- `TownHall`
- остальное

Дополнительное правило:

- если в свежей памяти есть что-то кроме `TownHall`, push не должен первым делом тоннелить в `TownHall`
- если свежих outer targets нет, AI использует точку подхода к базе, а не всегда центр `TownHall`

У `Harass` вместо старого "маленького push target" теперь отдельный выбор raid objective:

1. Видимые workers
2. Remembered workers около resource line
3. Mines / economic approach points
4. Outer buildings
5. Walkable outer-ring approach points
6. `TownHall` только как последний fallback

### На tactical уровне

`FindPreferredVisibleEnemy(...)` по-прежнему работает для обычных squad-потасовок.

Приоритет сейчас такой:

1. Вражеские юниты важнее зданий.
2. В `Harass` режиме workers получают сильный бонус по приоритету.
3. Для ranged юнитов боевые цели особенно приоритетны.
4. Если видимых юнитов нет, AI переходит на здания.

Для `Harass` поверх этого добавлен отдельный local target selection:

- workers имеют наивысший приоритет
- боевые юниты важны как блокеры рейда и как угроза при retreat
- outer buildings важнее `TownHall`
- `TownHall` получает сильный штраф, если рядом есть workers, defense units или outer buildings

### Переоценка цели во время атаки

Теперь есть еще одно важное правило поверх начального target selection:

- если AI-юнит уже атакует building
- и в непосредственный combat-контакт входит вражеский юнит
- то AI может переоценить цель и переключиться на этот юнит

Практический смысл такой:

- AI по-прежнему остается агрессивным
- но перестает выглядеть так, будто он игнорирует бой вокруг себя
- особенно это убирает странный сценарий, когда юнит уже получает удары в лицо, но все равно продолжает ковырять здание

## Micro

Текущий micro не «киберспортивный», но уже не совсем наивный.

### Что есть

- `Archer` старается не стоять вплотную без frontline
- `Catapult` отходит назад, если враг уже слишком близко и рядом нет frontline
- squad получает общую цель, а не хаотические разрозненные команды

### Чего пока нет

- kiting
- split против splash
- focus fire planner
- target lock по threat class
- flanking / surround logic
- path-aware arc movement

## Честность AI

Важно понимать точную грань «честности».

Что честно:

- AI использует sight юнитов и зданий
- хранит remembered positions
- чистит память, если снова увидел пустую точку
- не получает income bonus по difficulty

Что не совсем «полный human fog simulation»:

- suspected starting base игрока берется из `MapLayout`, потому что карта строится симметрично и стартовые позиции заранее известны сценарию

То есть AI не читает всю карту, но знает стартовую гипотезу о противнике.

## Взаимодействие с остальными системами

### Pathfinding

AI пользуется тем же pathfinding, что и игрок:

- multiple goal candidates
- soft tile penalties
- anti-stuck recovery
- near-goal slot selection для боя, ресурсов и стройки

Это важно, потому что крупные squad-команды AI теперь меньше склонны к идеально одинаковым путям.

### Fog / under attack

Under-attack reveal работает для игрока, не для AI. У AI своя knowledge model.

### Aggro rules

Важный gameplay-факт:

- прямые приказы игрока сейчас имеют приоритет над автоагром
- auto-retaliation для обычных юнитов ограничена `Idle`
- worker-поведение по удару вообще обслуживается отдельной логикой

Это влияет на ощущение боя против AI: player input стал более предсказуемым, а AI приходится играть против более «послушных» юнитов игрока.

## Слабые места текущей версии

### 1. AI все еще живет внутри GameSimulation

Это удобно на ранней стадии, но со временем:

- растет файл
- сложнее тестировать в изоляции
- тяжелее добавлять новые под-системы

### 2. Threat model упрощенный

`KnownEnemyPower()` и `KnownEnemyPressureNear()` простые и дешевые, но не очень умные.

AI пока не понимает, например:

- выгодность terrain
- узкие choke как отдельный фактор
- локальные fights по флангам
- timed reinforcements

### 3. Harass пока довольно короткий и неглубокий

- маленький squad
- нет рейдовых маршрутов
- нет приоритета по resource line topology
- нет особой логики выхода из опасной зоны кроме общего micro

### 4. Нет отдельного debug UI

Сейчас для тюнинга не хватает live-показа:

- текущего `AiState`
- squad power
- known enemy power
- confirmed base / last contact
- причин state transition

## Что стоит делать дальше

Если развивать AI дальше, самые полезные следующие шаги такие:

### Короткий горизонт

- добавить debug overlay по AI-state и memory
- тонко подстроить пороги `PushMinPower`, `RetreatRatio`, `HarassMinPower`
- улучшить target scoring внутри visible enemies
- сделать harass-маршруты вокруг базы, а не только прямой заход

### Средний горизонт

- вынести AI в отдельные классы:
  - `AiKnowledge`
  - `AiStrategicBrain`
  - `AiArmyManager`
  - `AiTacticalMicro`
- ввести явные production goals и composition targets
- добавить expansion logic
- добавить более явный defense planner

### Дальний горизонт

- map control evaluation
- multi-front logic
- reinforcement waves
- richer scouting loops
- behavior trees или GOAP/utility hybrid поверх текущих squad metrics

## Быстрый чек-лист для будущих изменений

Если меняется enemy AI, обычно надо проверить минимум это:

- AI все еще честно видит только то, что должен видеть
- difficulty не вернулась к income cheat
- `Push` и `Harass` все еще ощущаются по-разному
- `Harass` в early game действительно идет в worker line / mines / outer buildings, а не в один и тот же угол `TownHall`
- ранний `Push` и ранний `Harass` больше не летят слишком тупо прямо в `TownHall`, если рядом есть более логичные цели
- ranged не идут вперед melee без причины
- siege не умирает первой из-за плохого стейджинга
- AI переключается со зданий на подошедших вражеских юнитов в ближнем бою
- успешный рейд умеет вовремя disengage и перейти в recover, если локальная драка стала невыгодной
- AI умеет defend при раннем давлении
- после retreat AI реально regroup-ится, а не зацикливается

## Файлы, которые чаще всего придется трогать

- [Core/Simulation/GameSimulation.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Simulation/GameSimulation.cs)
- [Core/Data/GameSettings.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameSettings.cs)
- [Core/Data/GameConstants.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameConstants.cs)
- [Core/Data/GameDefinitions.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/Core/Data/GameDefinitions.cs)
- [AppRoot.cs](/C:/Users/golov/rofl_codex/RTS_spizjeno/rts-na-godote/AppRoot.cs)

Если документ начинает расходиться с кодом, источником истины всегда считается именно код, а этот файл нужно обновить под него.
