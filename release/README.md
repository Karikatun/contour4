# «Контур‑4» 0.2.36 из исходников

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
dotnet build $project -c Release -o ./build "-p:GameManaged=$managed" "-p:BepInExCore=$bep" "-p:RepoLibPath=$repoLib" "-p:Contour4ValuablesPath=$valuables" "-p:PathMap=$root=/_/contour4"
./prototype/ring/Pack.ps1 -Bundle "$bundles/contour4-ring.bundle" -LoadingBundle "$bundles/contour4-loading.bundle" -RoomsBundle "$bundles/contour4-rooms.bundle" -PreparedModulesBundle "$bundles/contour4-prepared-modules.bundle" -Plugin $assembly -Output ./build/SpaceJourney-Contour4-0.2.36.zip
~~~

Для воспроизведения сравните SHA‑256 записей архива по Test-PackagePair.ps1. SHA‑256 целого ZIP зависит также от метаданных архива, включая время записей.

## Пересобрать ассеты

Проекты Unity используются в редакторе 6000.3.17f1 (cf0352b38e81). Работайте с отдельной распакованной копией каждого проекта.

Проект release/room-authoring содержит оформленные префабы и исходники процедурного декора, включая восемь вариантов сферической камеры. Для пересборки двух бандлов выполните по очереди BuildRoomPrefabs.Build и BuildRoomPrefabs.BuildPrepared. Чтобы заново сгенерировать сферические префабы из каталога, запустите `prototype/procedural/NativeRaid/Build-PreparedModules.ps1` с параметром `-Module spherical-chamber-variants` и отдельным каталогом вывода. Для зала Мёбиуса используйте `-Module mobius-hall`; его перила входят в каталог коллизий. Переносите сгенерированные ассеты вместе с их `.meta`.

См. release/ring-authoring/README.md для сборки кольца V10. Оригинальные данные установленной игры и зависимости используются только через локальные ссылки. В этом архиве сохранены исходники проекта, ресурсы уровня и бандлы для точной повторной упаковки.

Для узкой проверки точной размещённой сцены в изолированном Unity-проекте используйте SpaceJourney.PreparedModulePackageChecks.RunMobiusSmoke (пять пройденных уровней, seed -32). RunMobiusOutletWitnesses проверяет естественную раскладку с парным потолочным выходом и отдельную ограниченную проверку недоступного партнёра штатным подбором пары и размещением заглушки; Run выполняет полный набор раскладок. Переменные CONTOUR4_ROOMS_BUNDLE и CONTOUR4_PREPARED_BUNDLE должны указывать на бандлы из проверяемого игрового ZIP.

