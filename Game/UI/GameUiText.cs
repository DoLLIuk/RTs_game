using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.UI;

internal static class GameUiText
{
    public const string MinimapTitle = "Миникарта";
    public const string CommandsHeader = "Команды";
    public const string CommandsIdleStatus = "Выберите юнита или здание, чтобы отдать приказ.";
    public const string SelectionOverviewTitle = "Сводка выбора";
    public const string ProductionTitle = "Производство";
    public const string CancelLastButton = "Отменить последний";

    public const string SelectionIdleTitle = "Готово к приказам";
    public const string SelectionIdleStats = "Нет активного выбора";
    public const string SelectionIdleSummary = "Выберите рабочего, чтобы начать экономику, затем открывайте военные здания и наращивайте армию.";
    public const string SelectionCardStartTitle = "Старт";
    public const string SelectionCardStartValue = "Рабочие";
    public const string SelectionCardStartDetail = "Сначала разгоните добычу, потом переходите к армии.";
    public const string SelectionCardMapTitle = "Карта";
    public const string SelectionCardMapValue = "Разведка";
    public const string SelectionCardMapDetail = "Туман войны скрывает угрозы и новые точки добычи.";
    public const string SelectionCardArmyTitle = "Армия";
    public const string SelectionCardArmyValue = "Баланс";
    public const string SelectionCardArmyDetail = "Смешивайте фронт, дальний бой, осадку и оборону.";
    public const string SelectionCardControlTitle = "Контроль";
    public const string SelectionCardControlValue = "ПКМ";
    public const string SelectionCardControlDetail = "ПКМ адаптируется под движение, атаку, добычу и точку сбора.";

    public const string BuildingCardTitle = "Строение";
    public const string StatusCardTitle = "Статус";
    public const string ProductionCardTitle = "Производство";
    public const string RallyCardTitle = "Точка сбора";
    public const string RoleCardTitle = "Роль";
    public const string OrderCardTitle = "Приказ";
    public const string CombatCardTitle = "Бой";
    public const string CargoCardTitle = "Груз";

    public const string EmptyValue = "Пусто";
    public const string FreeSlot = "Свободный слот";
    public const string EmptySlotTitle = "Слот";
    public const string EmptyGroupSlotDetail = "Здесь появится новый тип юнитов, когда группа станет разнообразнее.";
    public const string GroupSummarySeparator = "  |  ";

    public const string BuildingReadyState = "Готово";
    public const string BuildingFoundationState = "Фундамент";
    public const string BuildingConstructionWaiting = "Рабочий должен дойти до стройки, чтобы начался прогресс.";
    public const string BuildingActiveDetail = "Строение уже влияет на вашу базу.";
    public const string BuildingVulnerableDetail = "Недостроенное здание уязвимо и требует защиты.";
    public const string NoActiveTraining = "Нет активного найма.";
    public const string RallySetValue = "Задана";
    public const string RallyUnsetValue = "Не задана";
    public const string RallyCardEmptyDetail = "ПКМ задает точку выхода для новых юнитов.";
    public const string BuildingIdleRallyHint = "Производство простаивает. ПКМ по земле задает точку выхода.";
    public const string QueueSummaryPrefix = "Очередь: ";
    public const string QueueArrow = " -> ";
    public const string QueueEmpty = "Очередь пуста.";
    public const string ProductionCurrentBuildingIncomplete = "Сейчас: здание еще не завершено";
    public const string ProductionUnavailableUntilComplete = "Очередь станет доступна после завершения здания.";
    public const string ProductionCurrentIdle = "Сейчас: производство не запущено";
    public const string ProductionProgressIdle = "Прогресс: 0%  |  Очередь: 0/5";
    public const string CancelQueueButton = "Отменить хвост";
    public const string CancelQueueTooltip = "Отменить последний слот очереди и вернуть ресурсы за него.";
    public const string CancelQueueDisabledReason = "Отменяется только хвост очереди, активное производство не прерывается.";

    public const string AttackMoveButton = "Атака-движение [Q]";
    public const string AttackMoveTooltip = "Армия идет вперед и сама вступает в бой по пути.";
    public const string StopButton = "Стоп [X]";
    public const string StopTooltip = "Сбросить текущие приказы движения, добычи и атаки.";
    public const string CenterCameraButton = "К центру [Space]";
    public const string CenterCameraTooltip = "Быстро вернуть камеру к главной базе игрока.";

    public const string ReasonBuildingStillConstructing = "Здание еще строится.";
    public const string ReasonQueueFull = "Очередь уже заполнена.";
    public const string ReasonNotEnoughResources = "Не хватает ресурсов.";
    public const string ReasonNotEnoughSupply = "Не хватает лимита снабжения.";
    public const string DisabledPrefix = "Недоступно: ";

    public const string HintMatchFinished = "Матч завершен. Перезапустите бой или вернитесь в меню, чтобы сменить расу и сложность.";
    public const string HintAttackMoveActive = "Режим атака-движение активен. Выберите точку, чтобы армия шла вперед и сама вступала в бой.";
    public const string HintBuildingSelected = "Используйте панель команд или горячие клавиши для найма. ПКМ задает точку сбора.";
    public const string HintUnitsSelected = "ПКМ адаптируется под движение, атаку, добычу и стройку. Протягивание мышью выделяет отряды.";
    public const string HintNothingSelected = "Начните с экономики, затем открывайте военные здания и давите на вражескую базу.";

    public const string HoverNothing = "ничего";
    public const string ModeNormal = "обычный";
    public const string NoWinner = "нет";
    public const string StatusModeAttackMove = "Режим: атака-движение";
    public const string StatusModeNormal = "Режим: обычный";
    public const string StatusRallyUnset = "Точка сбора: не задана";
    public const string StatusRallyNone = "Точка сбора: нет выбранного здания";
    public const string StatusProductionIdle = "Производство: простаивает";
    public const string StatusProductionNone = "Производство: нет";
    public const string StatusQueueNone = "Очередь: 0/5";

    public const string GroupRoleWorker = "Экономика, строительство и экстренный ремонт базы.";
    public const string GroupRoleFootman = "Держит фронт и впитывает урон в начале боя.";
    public const string GroupRoleArcher = "Поддерживает издалека и добивает цели под фокусом.";
    public const string GroupRoleKnight = "Быстрый ударный юнит для флангов и прорывов.";
    public const string GroupRoleCatapult = "Разрушает здания и ломает плотные построения.";
    public const string GroupRoleDefault = "Состав отряда.";

    public const string UnarmedValue = "Без оружия";
    public const string UnitNonCombatDetail = "Этот юнит не предназначен для прямого боя.";
    public const string NoCargoDetail = "Сейчас ничего не несет.";

    public const string MessageAttackMoveArmed = "Режим атака-движение включен";
    public const string MessageAttackMoveCleared = "Режим атака-движение выключен";
    public const string MessageConstructionComplete = "Строительство завершено";
    public const string MessageUnderAttack = "Наши войска под атакой!";
    public const string MessageWrongProducer = "Это здание не может обучать выбранного юнита";
    public const string MessageQueueFailed = "Не удалось поставить юнита в очередь";
    public const string MessageQueueNothingToCancel = "Нечего отменять: активный юнит уже производится";
    public const string MessageNotEnoughResources = "Не хватает ресурсов";
    public const string MessagePlacementCancelled = "Размещение отменено";
    public const string MessageSelectWorkerFirst = "Сначала выберите рабочего";
    public const string MessageCannotPlaceBuilding = "Нельзя поставить здание в этой точке";
    public const string MessageOrdersCleared = "Приказы сброшены";
    public const string MessageVictory = "Победа!";
    public const string MessageDefeat = "Поражение!";
    public const string MarkerBuildingDown = "Здание уничтожено";
    public const string MarkerUnitDown = "Юнит уничтожен";
    public const string MarkerUnitReady = "Юнит готов";
    public const string MarkerAlert = "Тревога";
    public const string MarkerRally = "Точка сбора";
    public const string MarkerAttack = "Атака";
    public const string MarkerGather = "Добыча";
    public const string MarkerBuild = "Стройка";
    public const string MarkerDeposit = "Сдача";
    public const string MarkerFoundation = "Фундамент";
    public const string MarkerMove = "Движение";

    public const string MenuTitle = "Ashen Crown";
    public const string MenuSubtitle = "Godot edition";
    public const string MenuRaceLabel = "Race";
    public const string MenuDifficultyLabel = "Difficulty";
    public const string MenuAiProfileLabel = "Enemy AI";
    public const string MenuAlliance = "Alliance";
    public const string MenuHorde = "Horde";
    public const string MenuAiPush = "Push";
    public const string MenuAiHarass = "Harass";
    public const string MenuHint = "Single-player skirmish. Choose race, difficulty and enemy AI profile, then start the battle.";
    public const string MenuStartBattle = "Start Battle";
    public const string GameOverRestart = "Restart";
    public const string GameOverMenu = "Return To Menu";
    public const string PauseTitle = "Paused";
    public const string PauseResume = "Resume";
    public const string PauseSettings = "Settings";
    public const string PauseMainMenu = "Main Menu";
    public const string PauseSettingsTitle = "Settings";
    public const string PauseSettingsBack = "Back";
    public const string PauseDebugMode = "Debug mode";
    public const string PauseDebugModeHint = "Показывает FPS, тики и вражеских юнитов без изменения симуляции, логики и fog snapshot для AI.";
    public const string DebugOverlayFormat = "DEBUG  |  FPS: {0}  |  TPS: {1}  |  Ticks: {2}";

    public const string ResourceGoldLabel = "Gold";
    public const string ResourceLumberLabel = "Lumber";
    public const string ResourceFoodLabel = "Food";

    public const string LastCommandNone = "нет";

    public static string Resource(ResourceType type) => type switch
    {
        ResourceType.Gold => "золота",
        ResourceType.Lumber => "дерева",
        _ => type.ToString()
    };

    public static string ResourceShort(ResourceType type) => type switch
    {
        ResourceType.Gold => "золото",
        ResourceType.Lumber => "дерево",
        _ => type.ToString()
    };

    public static string UnitStateLabel(UnitState state) => state switch
    {
        UnitState.Idle => "Ожидает",
        UnitState.Move => "Движется",
        UnitState.AttackMove => "Атака-движение",
        UnitState.Attack => "Сражается",
        UnitState.Gather => "Добывает",
        UnitState.ReturnCargo => "Возвращает ресурсы",
        UnitState.Build => "Строит",
        UnitState.Dead => "Уничтожен",
        _ => state.ToString()
    };

    public static string UnitStateHintText(UnitState state) => state switch
    {
        UnitState.Idle => "Ждет следующий приказ и готов быстро сменить задачу.",
        UnitState.Move => "Следует по маршруту к указанной точке.",
        UnitState.AttackMove => "Продвигается вперед и сам вступает в бой по пути.",
        UnitState.Attack => "Уже ведет бой с назначенной целью.",
        UnitState.Gather => "Работает на ресурсе и готовит следующую поставку.",
        UnitState.ReturnCargo => "Несет груз в ратушу для сдачи.",
        UnitState.Build => "Идет к стройке или уже возводит фундамент.",
        UnitState.Dead => "Юнит выбыл из боя.",
        _ => "Ожидает команды."
    };

    public static string MovementRecoveryLabel(MovementRecoveryKind kind) => kind switch
    {
        MovementRecoveryKind.None => "none",
        MovementRecoveryKind.LocalAvoidance => "avoid",
        MovementRecoveryKind.HeadOnAvoidance => "head-on",
        MovementRecoveryKind.CohortLaneChange => "lane-change",
        MovementRecoveryKind.CohortFollow => "follow",
        MovementRecoveryKind.AllyPassThrough => "phase-through",
        MovementRecoveryKind.CongestionSwitch => "terminal-switch",
        MovementRecoveryKind.LightRepath => "repath",
        MovementRecoveryKind.HeavyReroute => "heavy-reroute",
        MovementRecoveryKind.StaticSlide => "slide",
        _ => kind.ToString()
    };

    public static string UnitRole(UnitKind kind) => kind switch
    {
        UnitKind.Worker => "Рабочий развивает экономику, возводит здания и чинит базу.",
        UnitKind.Footman => "Пехота держит фронт и принимает основной урон на себя.",
        UnitKind.Archer => "Стрелок поддерживает армию из-за спин фронтлайна.",
        UnitKind.Knight => "Тяжелая конница обходит фланги и давит по ключевым целям.",
        UnitKind.Catapult => "Осадная машина ломает укрепления и плотные скопления войск.",
        _ => "Боевая единица."
    };

    public static string BuildingRole(BuildingKind kind) => kind switch
    {
        BuildingKind.TownHall => "Главная база: обучает рабочих, принимает ресурсы и задает точку расширения.",
        BuildingKind.Farm => "Увеличивает лимит снабжения, чтобы армия могла расти дальше.",
        BuildingKind.Barracks => "Открывает основную армию: пехоту, стрелков и ударные отряды.",
        BuildingKind.Workshop => "Дает доступ к осадным и продвинутым боевым опциям.",
        BuildingKind.Tower => "Стационарная оборона, контролирующая подходы к базе.",
        _ => "Ключевое строение базы."
    };

    public static Color BuildingAccent(BuildingKind kind) => kind switch
    {
        BuildingKind.TownHall => new Color(0.82f, 0.72f, 0.4f),
        BuildingKind.Farm => new Color(0.55f, 0.9f, 0.56f),
        BuildingKind.Barracks => new Color(0.96f, 0.62f, 0.42f),
        BuildingKind.Workshop => new Color(0.62f, 0.76f, 1f),
        BuildingKind.Tower => new Color(0.8f, 0.6f, 1f),
        _ => new Color(0.72f, 0.78f, 0.85f)
    };

    public static Color UnitAccent(UnitKind kind) => kind switch
    {
        UnitKind.Worker => new Color(0.48f, 0.87f, 1f),
        UnitKind.Footman => new Color(1f, 0.72f, 0.45f),
        UnitKind.Archer => new Color(0.55f, 0.94f, 0.56f),
        UnitKind.Knight => new Color(0.9f, 0.62f, 0.5f),
        UnitKind.Catapult => new Color(0.7f, 0.7f, 0.84f),
        _ => new Color(0.72f, 0.78f, 0.85f)
    };

    public static string BuildingDisplayName(BuildingKind kind, Race race)
    {
        return GameDefinitions.BuildingLabel(kind, race);
    }

    public static string UnitDisplayName(UnitKind kind, Race race)
    {
        return GameDefinitions.UnitLabel(kind, race);
    }

    public static string BuildingTooltip(BuildingKind kind, Race race)
    {
        var definition = GameDefinitions.Buildings[kind];
        var label = BuildingDisplayName(kind, race);
        var supply = definition.FoodCapBonus > 0 ? $" Дает +{definition.FoodCapBonus} к лимиту." : string.Empty;
        return $"{label}. {BuildingRole(kind)} Стоимость: {definition.CostGold} золота, {definition.CostLumber} дерева.{supply}";
    }

    public static string UnitTooltip(UnitKind kind, Race race)
    {
        var definition = GameDefinitions.Units[kind];
        var label = UnitDisplayName(kind, race);
        var requires = definition.Requires.HasValue
            ? $" Требует {BuildingDisplayName(definition.Requires.Value, race)}."
            : string.Empty;
        return $"{label}. {UnitRole(kind)} Стоимость: {definition.CostGold} золота, {definition.CostLumber} дерева. Снабжение: {definition.Food}.{requires}";
    }

    public static string ReasonRequiresBuilding(string buildingLabel) => $"Нужно здание: {buildingLabel}.";
    public static string TrainButtonText(string unitLabel, string hotkey) => $"{unitLabel} [{hotkey}]";
    public static string BuildButtonText(string buildingLabel, string hotkey) => $"{buildingLabel} [{hotkey}]";
    public static string HintPlacingBuilding(string buildingLabel) => $"Размещение: {buildingLabel}. ЛКМ подтверждает, ПКМ или Esc отменяет.";
    public static string DisabledReasonLine(string reason) => $"{DisabledPrefix}{reason}";
    public static string TooltipWithReason(string description, string reason) => $"{description}\n{DisabledReasonLine(reason)}";
    public static string HoverStatusText(string description, bool enabled, string disabledReason) => enabled ? description : $"{description}  {DisabledReasonLine(disabledReason)}";

    public static string ActivityLine(string lastCommand, string hovered, Vector2I hoveredTile, string mode, string winner)
    {
        return $"Последний приказ: {lastCommand}  |  Наведение: {hovered}  |  Тайл: {hoveredTile.X},{hoveredTile.Y}  |  Режим: {mode}  |  Победитель: {winner}";
    }

    public static string DebugUnitMovement(int unitId, string cohort, string movementMode, int pathCount, int stuckMs, int stallMs, string recovery, bool passThroughActive)
    {
        return $"Unit #{unitId}  |  cohort: {cohort}  |  mode: {movementMode}  |  path: {pathCount}  |  stuck: {stuckMs}ms  |  stall: {stallMs}ms  |  recovery: {recovery}  |  pass-through: {(passThroughActive ? "on" : "off")}";
    }

    public static string QueueLead(string unitLabel, int progressPercent) => $"{unitLabel}: {progressPercent}%.";
    public static string ProductionConstructionProgress(int progressPercent) => $"Прогресс строительства: {progressPercent}%";
    public static string ProgressPercent(int progressPercent) => $"{progressPercent}%";
    public static string QueueSlotLabel(int index) => $"Слот {index}";
    public static string ProductionCurrent(string unitLabel) => $"Сейчас: {unitLabel}";
    public static string ProductionProgress(int progressPercent, int queueCount) => $"Прогресс: {progressPercent}%  |  Очередь: {queueCount}/5";
    public static string CancelLastQueuedHint(string unitLabel) => $"Отменить последний слот: {unitLabel} с возвратом ресурсов.";
    public static string StatusModePlacement(string buildingLabel) => $"Режим: размещение {buildingLabel}";
    public static string StatusRally(int x, int y) => $"Точка сбора: {x},{y}";
    public static string StatusProduction(string unitLabel) => $"Производство: {unitLabel}";
    public static string StatusQueue(int queueCount) => $"Очередь: {queueCount}/5";
    public static string BuildingConstructing(int progressPercent) => $"Строится {progressPercent}%";
    public static string BuildingConstructionInProgress(int progressPercent) => $"Стройка завершена на {progressPercent}%. Защитите ее, пока она уязвима.";
    public static string RallyPointSummary(int x, int y) => $"Точка сбора: {x},{y}.";
    public static string RallyPointCardDetail(int x, int y) => $"Новые юниты идут к {x},{y}.";
    public static string UnitCarrying(int amount, string resourceLabel) => $"Несет {amount} {resourceLabel}.";
    public static string UnitDamage(int amount) => $"{amount} урона";
    public static string UnitCombatDetail(bool isRanged, int range) => $"{(isRanged ? "Дальний" : "Ближний")} бой, дальность {range}.";
    public static string UnitCargoDetail(string resourceLabel) => $"Держит {resourceLabel} для сдачи в базу.";
    public static string GroupCardValue(int count, int hp, int maxHp) => $"x{count}  HP {hp}/{maxHp}";
    public static string GroupSelectionTitle(int count) => $"Выбрано: {count} юнитов";
    public static string GroupSelectionStats(int hp, int maxHp) => $"HP отряда {hp}/{maxHp}";
    public static string GroupSummaryEntry(string unitLabel, int count) => $"{unitLabel} x{count}";
    public static string HoverUnit(UnitKind kind, Race race, int id) => $"{UnitDisplayName(kind, race)} #{id}";
    public static string HoverBuilding(BuildingKind kind, Race race, int id) => $"{BuildingDisplayName(kind, race)} #{id}";
    public static string HoverResource(ResourceType type, int id) => $"{ResourceShort(type)} #{id}";
    public static string PlacementModeShort(string buildingLabel) => $"стройка {buildingLabel}";
    public static string WinnerLabel(GameSide winner) => winner == GameSide.Player ? "игрок" : "враг";

    public static string ResourceGold(int amount) => $"{ResourceGoldLabel}: {amount}";
    public static string ResourceLumber(int amount) => $"{ResourceLumberLabel}: {amount}";
    public static string ResourceFood(int food, int foodCap) => $"{ResourceFoodLabel}: {food}/{foodCap}";
    public static string GameOverTitle(GameSide winner) => winner == GameSide.Player ? "Победа" : "Поражение";
    public static string BattleStarted(Race race, string difficultyLabel, AiProfile aiProfile) => $"Бой начался: {race} / {difficultyLabel} / {AiProfileLabel(aiProfile)}";
    public static string UnitQueued(string unitLabel) => $"В очередь поставлен: {unitLabel}";
    public static string QueueCanceled(string unitLabel) => $"Очередь: отменен {unitLabel}";
    public static string PlacingBuilding(string buildingLabel) => $"Размещение: {buildingLabel}";

    public static string PlacementIssueMessage(BuildingPlacementIssue issue, BuildingKind kind, Race race)
    {
        return issue switch
        {
            BuildingPlacementIssue.None => PlacingBuilding(BuildingDisplayName(kind, race)),
            BuildingPlacementIssue.OutOfBounds => "Нельзя строить за пределами карты",
            BuildingPlacementIssue.Blocked => "Эта зона занята или заблокирована",
            BuildingPlacementIssue.InsufficientResources => MessageNotEnoughResources,
            _ => MessageCannotPlaceBuilding
        };
    }

    public static string CommandRally(string point) => $"точка сбора @ {point}";
    public static string CommandAttackUnit(int id) => $"атака юнита #{id}";
    public static string CommandAttackBuilding(int id) => $"атака здания #{id}";
    public static string CommandGather(ResourceType type, int id) => $"добыча {ResourceShort(type)} #{id}";
    public static string CommandBuild(int id) => $"строить #{id}";
    public static string CommandReturnCargo(int id) => $"сдать ресурсы #{id}";
    public static string CommandSelectUnit(int id) => $"выбран юнит #{id}";
    public static string CommandSelectBuilding(int id) => $"выбрано здание #{id}";
    public static string CommandSelectNone() => "выбор снят";
    public static string CommandBoxSelect(int count) => $"рамка выбора: {count}";
    public static string CommandQueue(UnitKind kind, Race race) => $"очередь: {UnitDisplayName(kind, race)}";
    public static string CommandCancelQueue(UnitKind kind, Race race) => $"отмена очереди: {UnitDisplayName(kind, race)}";
    public static string CommandPlace(BuildingKind kind, Race race) => $"подготовка стройки: {BuildingDisplayName(kind, race)}";
    public static string CommandStartFoundation(BuildingKind kind, int id, Race race) => $"начата стройка: {BuildingDisplayName(kind, race)} #{id}";
    public static string CommandMove(string point) => $"движение @ {point}";
    public static string CommandAttackMove(string point) => $"атака-движение @ {point}";
    public static string CommandStop() => "стоп";

    public static string AiProfileLabel(AiProfile profile) => profile switch
    {
        AiProfile.Push => "Push AI",
        AiProfile.Harass => "Harass AI",
        _ => profile.ToString()
    };
}
