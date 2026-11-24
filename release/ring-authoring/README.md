# Кольцо V10 — проект Unity

Открывайте отдельную копию этой папки в Unity 6000.3.17f1 (cf0352b38e81). Исходная сцена лежит в Assets/Contour4Ring/Furniture Prototype V10.unity. Папка Assets содержит её материалы, меши, комнаты, мосты, файлы .meta и финальный префаб.

Из PowerShell 7 выполните редактором в пакетном режиме метод BuildRingV10.Build. Он переносит маркеры трёх ценностей на рабочие поверхности, объединяет неподвижные непрозрачные детали мебели по материалу и экспортирует префаб и два шейдера. Результат находится в BundleOutput/contour4-ring.bundle. Для повторной упаковки 0.2.27 используйте точный проверенный бандл в ../bundles.

~~~powershell
$editor = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe'
$project = (Get-Location).Path
$args = @('-batchmode','-nographics','-quit','-projectPath',('"' + $project + '"'),
    '-executeMethod','BuildRingV10.Build','-logFile',('"' + "$project/build.log" + '"'))
$run = Start-Process -FilePath $editor -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
if ($run.ExitCode -ne 0) { throw "Unity exited with $($run.ExitCode)" }
~~~

Для проверки замените BuildRingV10.Build на BuildRingV10.Verify. Перед запуском задайте CONTOUR4_RING_VERIFY_BUNDLE с абсолютным путём к проверяемому бандлу и CONTOUR4_RING_BASELINE_BUNDLE с путём к кольцу, извлечённому из пакета 0.2.26-full-visual-loaded-cart. Ожидаемый SHA‑256 исходного кольца — FF6CC6EF6920E9CCCC844F4DE655338E97F262676464E3E7A92DB518D7276E13. Скрипт сравнивает стыки и маркеры, проверяет штатные опоры предметов, закрытие боковин и полные циклы открытия и закрытия фасадов. Отчёт bundle-verification.txt появляется в родительской папке проекта.