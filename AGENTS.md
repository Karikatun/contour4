# Работа над «Контуром-4»

## Откуда брать базу для ветки

Рабочая папка `space-journey` содержит несколько модов и локальные сборки. Публикации 22 и 24 ноября 2026 года делали из отдельных каталогов в `%TEMP%`. 27 ноября в корневом Git папки `space-journey` создали коммит `e4533ff` с отдельной историей. Перед созданием ветки карты сверь URL через `git remote -v`, SHA базы с нужной веткой GitHub и версию с локальным архивом исходников в `prototype/ring/dist`. Для ветки от опубликованной базы проверь родство через `git merge-base --is-ancestor <SHA-базы> HEAD`. [Репозиторий карты](https://github.com/Karikatun/contour4) · [Репозиторий предметов](https://github.com/Karikatun/contour4-valuables)

## Delegated Engineering

Для реализации, проверки и интеграции модулей «Контура-4» обязателен навык `delegated-engineering`. Перед началом работы прочитай `SKILL.md` и необходимые ссылки из него. Используй доступную локальную копию навыка. Если её нет в окружении, прочитай исходный `SKILL.md` и нужные файлы `references/` в репозитории автора. [Руководство навыка](https://github.com/Karikatun/agent-skills/blob/main/skills/delegated-engineering/README.md) · [Инструкции SKILL.md](https://github.com/Karikatun/agent-skills/blob/main/skills/delegated-engineering/SKILL.md)

Реализуй комнаты последовательно. Для каждого модуля задай границы изменений, проверь стыки и габариты по каталогу, зафиксируй результаты выполненных проверок. Для работы над процедурной картой используй её [каталог модулей](openspec/changes/contour4-procedural/module-catalog.md), [дизайн](openspec/changes/contour4-procedural/design.md) и [текущий статус](openspec/changes/contour4-procedural/state.json).

## Инструменты для модов R.E.P.O.

При работе с материалами, моделями, эффектами, навигацией или процедурным декором смотри [рекомендации по инструментам и проверкам](REPO-LEVEL-PLAN.md#инструменты-для-модов-repo). Подбирай инструмент под задачу и проверяй результат на конкретном модуле или ассете в Unity.
