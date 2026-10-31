<p align="center"><img src="docs/logo.png" width="96" alt="CS2/OPT"></p>

<h1 align="center">CS2 Optimizer</h1>

<p align="center">Оптимизация Windows и настроек Counter-Strike 2 в одном окне: пресеты графики, конструктор прицела, сенса, autoexec, радары карт с позициями и ватермарка с FPS поверх игры.</p>

<p align="center"><img src="docs/system.png" width="820" alt="Состояние системы"></p>

## Скачать

**[Последний релиз → CS2Optimizer.zip](../../releases/latest)** — один `CS2Optimizer.exe`, ничего ставить не нужно (.NET уже внутри). Windows 10/11, x64.

> Программа без цифровой подписи, поэтому Windows покажет SmartScreen: **«Подробнее» → «Выполнить в любом случае»**.

## Что умеет

| Раздел | Что делает |
|---|---|
| **01 Система** | Проверяет питание, план электропитания, видеокарту для CS2 (на ноутбуках с двумя GPU), двухканальную память, игровой режим, фоновую запись Xbox, планирование GPU, оптимизацию оконных игр. Почти всё исправляется одной кнопкой. |
| **02 Графика** | Пресеты «Максимум FPS / Баланс / Качество» для `cs2_video.txt` с таблицей «сейчас → станет» и резервной копией. |
| **03 Параметры запуска** | Читает текущую строку из Steam и собирает новую с объяснением каждого параметра. |
| **04 Очистка фона** | Показывает, что ест память, и закрывает выбранное (системные процессы, Steam и CS2 не трогает). |
| **05 Прицел** | Конструктор с превью и командами в новом формате (после обновления прицела в сентябре 2026). |
| **06 Сенса и мышь** | eDPI, см/360°, перевод сенсы из Valorant / Overwatch 2 / CoD / Apex, проверка акселерации мыши. |
| **07 Конфиг** | Полезные команды (лимит FPS, пинг и FPS в HUD, радар, звук) отдельным блоком в `autoexec.cfg`. |
| **08 Карты** | Радар каждой карты с официальными зонами игры, зум колесом, плюс справочник колаутов игроков с русским сленгом. |
| **09 Ватермарка** | Плашка поверх игры: FPS, загрузка и температура CPU/GPU. Стиль, цвет, размер и угол настраиваются. |
| **10 Настройки** | Трей, автозапуск с Windows, ватермарка при старте, кеш карт, сброс. |

<p align="center">
  <img src="docs/maps.png" width="49%" alt="Карты">
  <img src="docs/maps-zoom.png" width="49%" alt="Зум к позиции">
</p>
<p align="center">
  <img src="docs/crosshair.png" width="49%" alt="Прицел">
  <img src="docs/watermark.png" width="49%" alt="Ватермарка">
</p>

## Карты и позиции

- **Радары и зоны** приложение берёт прямо из файлов установленной CS2 (`env_cs_place` — те же зоны, что игра пишет рядом с ником тиммейта). Ничего не скачивается, после патчей обновляется само.
- **Колауты игроков** — около 400 позиций по 10 картам с русскими названиями и описаниями. Сверены по [csdb.gg](https://csdb.gg/callouts), [cs2pulse.com](https://cs2pulse.com/callouts), [profilerr.net](https://profilerr.net/all-mirage-callouts) и [hotspawn.com](https://www.hotspawn.com/) в октябре 2026.
- Active Duty (октябрь 2026): Ancient, Anubis, Cache, Dust II, Inferno, Mirage, Nuke. Overpass, Train и Vertigo тоже есть.

## Совместимость

| | |
|---|---|
| Видеокарта NVIDIA | загрузка и температура GPU |
| Видеокарта AMD / Intel | загрузка GPU через счётчики Windows; температура — «—» (Windows её не отдаёт без драйверов производителя) |
| Температура CPU | с датчика платы (ACPI), есть не на всех ПК |
| FPS в ватермарке | любая видеокарта (DirectX 11/12), нужен запуск ватермарки от администратора; с `-vulkan` может не считаться |
| Поверх игры | гарантированно в режиме «Окно без рамки» |

## Безопасность и VAC

Приложение **не трогает процесс игры и её память**. Оно только меняет файлы настроек (`cs2_video.txt` — с резервной копией, `autoexec.cfg` — отдельным блоком), читает файлы карт и меняет настройки Windows по кнопке. Ватермарка — обычное прозрачное окно поверх экрана, FPS считается системным трейсингом Windows (как в PresentMon), без инъекций.

## Сборка из исходников

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```

Нужен .NET 10 SDK. Иконка собирается скриптом `tools/make-icon.ps1` из `Themes/Logo.xaml`.

Используются [ValvePak и ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) (MIT) для чтения файлов Source 2 и [TraceEvent](https://github.com/microsoft/perfview) (MIT) для счётчика FPS.

## Лицензия

[MIT](LICENSE). Counter-Strike 2 и все материалы игры принадлежат Valve; проект не связан с Valve.
