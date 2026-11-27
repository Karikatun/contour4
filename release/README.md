# «Контур‑4» 0.3.7 · ветка сложности рейда

Для сборки распакуйте архив исходников и откройте PowerShell 7 в его корне. Используйте .NET SDK 10.0.404 и локальные зависимости из R.E.P.O. 0.5.2, BepInExPack 5.4.2305, REPOLib 4.5.1 и Contour4Valuables 0.2.4. Контрольные суммы зависимостей и ресурсов перечислены в release/input-lock.json.

## Собрать локальный ZIP

Укажите пути к каталогу REPO_Data/Managed версии 0.5.2, к BepInEx/core, к REPOLib.dll и к Contour4Valuables.dll. Последняя DLL находится в игровом ZIP отдельного мода. Для сборки используются точные AssetBundle из release/bundles.

~~~powershell
$root = (Get-Location).Path
$managed = '<REPO_Data/Managed из R.E.P.O. 0.5.2>'
$bep = '<BepInEx/core из BepInExPack 5.4.2305>'
$repoLib = '<REPOLib.dll из REPOLib 4.5.1>'
$valuables = '<Contour4Valuables.dll из Contour4Valuables 0.2.4>'
$assembly = './build/Contour4.dll'
$bundles = './release/bundles'
$project = './prototype/ring/Plugin/Contour4.csproj'
$revision = (Get-Content ./release/input-lock.json -Raw | ConvertFrom-Json).sourceRevisionId
dotnet build $project -t:Rebuild -c Release -o ./build "-p:GameManaged=$managed" "-p:BepInExCore=$bep" "-p:RepoLibPath=$repoLib" "-p:Contour4ValuablesPath=$valuables" "-p:PathMap=$root=/_/contour4" "-p:SourceRevisionId=$revision" "-p:DebugType=None"
./prototype/ring/Pack.ps1 -Bundle "$bundles/contour4-ring.bundle" -LoadingBundle "$bundles/contour4-loading.bundle" -RoomsBundle "$bundles/contour4-rooms.bundle" -PreparedModulesBundle "$bundles/contour4-prepared-modules.bundle" -Plugin $assembly -Output ./build/Contour4-0.3.7.zip
~~~

Перед сборкой сверьте хеш `REPOLib.dll` с `localReferences` в `release/input-lock.json`: игровая копия после перенаправления имеет тот же MVID, но другие байты и не подходит как исходная зависимость. Для воспроизведения сравните SHA‑256 записей архива по Test-PackagePair.ps1. `SourceRevisionId` воспроизводит метку базовой ревизии в метаданных обеих DLL при сборке из распакованного ZIP. SHA‑256 целого ZIP зависит также от метаданных архива, включая время записей. Для 0.3.7 эта метка указывает на базовый коммит `2d6cb0ec6a753324422a6188058e47d8e86510c1`; изменения кандидата содержатся в ZIP исходников и не обозначают отдельный коммит.

Для проверки новых маршрутов запустите `prototype/ring/Test-PreparedPackage.ps1 -MapPackage <архив карты> -Output <новый каталог> -ExtractionBridges`. Проверка загружает бандлы из указанного ZIP и собирает рейды на трёх seed при 1, 4, 5, 8, 9 и 14 пройденных уровнях. Она проверяет ширину и непрерывность пути для ручного груза, провал и ориентацию подхода к штатной рампе. Для оценки масштаба снимите обычную камеру над серединой настила и широкий ракурс, где видны обе границы настила и шахта. В отдельной копии 0.5.2 пройдите мост в обе стороны с ручной ценностью. Замер рампы и входной площадки дополняется замером самого узкого участка.

Для проверки поворотной кабины подготовьте отдельный проект командой `prototype/procedural/NativeRaid/Test-NativeRaid.ps1 -Output <новый каталог> -PrepareOnly`. Откройте вложенный проект `Unity` в редакторе 6000.3.17f1 с параметрами `-batchmode -nographics -quit -executeMethod Contour4.NativeRaid.Editor.RaidQuotaRotaryChecks.Run`. В журнале ищите `CONTOUR4 ROTARY QUOTA PASSED`. Стенд проверяет стыковку, вызов с обеих площадок, возврат и восстановление после потери контроллера. В этом же проекте `Contour4.NativeRaid.Editor.RaidRotaryRuinsChecks.Run` проверяет свободный объём шахты, уклоны и стыки опор. Видимость, штатный ввод и сдачу груза проверяют в отдельной копии R.E.P.O. на seed −31, −30 и −29.

Отделку ближней площадки проверяйте в одиночном рейсе на seed −31 после первого уровня. Сравните входной кадр при том же FOV с базовым и сделайте боковой кадр при штатном свете. Для каждого укрытия проверьте проход с удержанием приседания в обе стороны и линии обзора с соответствующего конца пандуса.

При добавлении плоской секции стены проверяйте блок свойств каждого рендера в игре. После `Projection.Flat` расстояние вдоль стены лежит в координате `p.x`, даже если в комнате стена тянется по оси Z. Для такой проекции профиль должен выбирать `p.xy`. У накладок вокруг кольца одна ссылка на материал `Contour-4 painted plaster dado` оставляла блок проекции пустым. Сверяйте карту `_WearMap`, локальный кадр, `_WallAcross` и `_WearProfile.w` на фасаде и на его накладках, затем снимайте их штатной камерой. [Наблюдения по версии 0.3.3](../build/evidence/rotary-lift-033-native/REPORT.md)

## Пересобрать ассеты

Проекты Unity используются в редакторе 6000.3.17f1 (cf0352b38e81). Работайте с отдельной распакованной копией каждого проекта.

Проект release/room-authoring содержит оформленные префабы и исходники процедурного декора, включая восемь вариантов сферической камеры. Для пересборки двух бандлов выполните по очереди BuildRoomPrefabs.Build и BuildRoomPrefabs.BuildPrepared. Первый метод добавляет шейдер износа и три карты Poly Haven из папки Materials/SurfaceTextures. Их лицензии, авторы и контрольные суммы перечислены в [surface-textures.json](surface-textures.json). Чтобы заново сгенерировать сферические префабы из каталога, запустите `prototype/procedural/NativeRaid/Build-PreparedModules.ps1` с параметром `-Module spherical-chamber-variants` и отдельным каталогом вывода. Для зала Мёбиуса используйте `-Module mobius-hall`. Его перила входят в каталог коллизий. Переносите сгенерированные ассеты вместе с их `.meta`.

См. release/ring-authoring/README.md для сборки кольца V10. Оригинальные данные установленной игры и зависимости используются только через локальные ссылки. В этом архиве сохранены исходники проекта, ресурсы уровня и бандлы для точной повторной упаковки.

Для узкой проверки точной размещённой сцены в изолированном Unity-проекте используйте SpaceJourney.PreparedModulePackageChecks.RunMobiusSmoke (пять пройденных уровней, seed -32). RunMobiusOutletWitnesses проверяет естественную раскладку с парным потолочным выходом и отдельную ограниченную проверку недоступного партнёра штатным подбором пары и размещением заглушки; Run выполняет полный набор раскладок. Переменные CONTOUR4_ROOMS_BUNDLE и CONTOUR4_PREPARED_BUNDLE должны указывать на бандлы из проверяемого игрового ZIP.

Скрипт `release/tests/Verify-RoomSurfaceWear.ps1 -Baseline <Contour4-0.2.37.zip> -Candidate <Contour4-0.2.38.zip> -Output <каталог>` загружает ассеты из обоих архивов в отдельный графический проект Unity. Он сохраняет парные кадры и сравнивает коллайдеры, свет и маркеры. В проверке стартовых зон собирается авторская геометрия дока и гаража с тестовыми заглушками игровых типов.

Скрипт `release/tests/Verify-RoomBloodTraces.ps1 -Baseline <Contour4-0.2.38.zip> -Candidate <Contour4-0.2.40.zip> -Output <каталог>` проверяет следы в двух естественных раскладках. Он сравнивает опоры, коллайдеры, свет и маркеры и сохраняет парные кадры с одинаковым проверочным фонарём. Маски следов генерируются кодом `RoomBloodTraces.cs` при загрузке сцены.

Скрипт `release/tests/Verify-BrokenEquipment.ps1 -Baseline <Contour4-0.2.40.zip> -BaselineSource <распакованные исходники 0.2.40> -Candidate <Contour4-0.2.41.zip> -Output <каталог>` сравнивает две версии сцены. Он проверяет глубину отверстий, прежние габариты и положение деталей после сдвига координат. Обе сцены сняты с одинаковым проверочным фонарём.

Скрипт `release/tests/Verify-ReadableMaterials.ps1 -Baseline <Contour4-0.2.42.zip> -BaselineSource <распакованные исходники 0.2.42> -Candidate <Contour4-0.2.43.zip> -Output <каталог>` сверяет два набора материалов и снимает шесть пар кадров с одинаковым авторским освещением. Проверка включает сферическую камеру. Фактура дерева и крупного заполнителя бетона создаётся шейдером. Три исходные карты Poly Haven перечислены в `surface-textures.json`.

Для сравнения 0.2.46 и 0.2.47 используйте этот же скрипт с ключом `-BothHaveMaterialFamilies` и архивами соответствующих версий. Он сравнит назначение материалов и снимет шесть пар кадров в одинаковом свете. В 0.2.47 снижена пятнистость материалов, а на бетоне используются более мелкие неровные зёрна.

В 0.2.48 добавлена проверка каждого слота материала в собранном каталоге и его запасном варианте. Запустите `release/tests/Verify-MaterialCoverage.ps1 -Package <архив карты> -Output <каталог> -EditionSource <распакованные исходники того же архива>` для обеих версий. `compare-coverage.py <база> <кандидат> <вывод>` сверит состав сцен, материалы и контрольные сигнатуры геометрии и света. Для проверки авторского стартового модуля Unity использует ту же фабрику моделей, отделённую от вызова, который ищет штатный фургон в запущенной игре.

Скрипт `release/tests/Verify-EquipmentLoot.ps1 -Baseline <Contour4-0.2.43.zip> -BaselineSource <распакованные исходники 0.2.43> -Candidate <Contour4-0.2.44.zip> -Output <каталог>` проверяет опору, габарит и свободный ход предмета в обеих нишах, отклонение неподходящих предметов и совпадение обычных точек, света и проверенных проходов. Геометрические пробники используют размеры штатных часов и кубка. Фактические предметы и штатный захват дополнительно проверяются в игре.

Для независимой проверки стен выполните release/tests/Invoke-WallCatalogAudit.ps1 для архивов .51 и .52, передав -Package, -EditionSource и -Output для каждой версии. Затем запустите compare-wall-catalog.py <вывод .51> <вывод .52> <сводка>; для кадров требуются Python Pillow и Segoe UI в Windows.

Для проверки парных укрытий используйте `release/tests/Verify-PairRacks.ps1 -SourceArchive <ZIP исходников> -Package <игровой ZIP> -Output <новый каталог>`. Скрипт проверяет подготовленные комнаты и запасную геометрию в изолированном Unity Editor; отчёт и входные хеши сохраняются в каталоге вывода. Для регрессий ранее проверенной мебели доступен `Verify-PreviousRegressions.ps1`.
