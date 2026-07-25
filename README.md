# ZapretWrapper

Лёгкая GUI-обёртка для [zapret2](https://github.com/bol-van/zapret2) — утилиты обхода DPI на Windows 10/11 x64.

## Что это

zapret2 — мощный инструмент обхода блокировок YouTube, Discord, Telegram и других сервисов через манипуляции с TCP/UDP пакетами. Но настройка через `.cmd` файлы и параметры командной строки неудобна для обычного пользователя.

**ZapretWrapper** — это нативное WPF-приложение (C# / .NET 8), которое:
- Запускает и останавливает `winws2.exe` от администратора одной кнопкой
- Подбирает рабочую стратегию обхода через встроенный тестер
- Имеет переключаемые темы (светлая/тёмная/системная)
- Прячет сложность параметров командной строки за пресетами

## Статус

🚧 **В разработке (MVP).** Готово: GUI-каркас, темы, базовые элементы. В работе: бэкенд (стратегии, тестер, запуск winws2).

## Требования

- Windows 10/11 x64
- .NET 8 Runtime
- Установленный zapret2 (см. [zapret-win-bundle](https://github.com/bol-van/zapret-win-bundle))

## Структура проекта

```
Zapret-wrapper/
├── src/ZapretWrapper/         # Основной проект
│   ├── Views/                 # UserControl-ы страниц
│   ├── Controls/              # Кастомные контролы (MetricRing)
│   ├── Styles/                # Темы, ThemeManager, WindowChrome
│   └── ViewModels/            # MVVM-модели
├── Zapret-wrapper.sln
└── docs/                      # Скриншоты и документация
```

## Лицензия

MIT — см. [LICENSE](LICENSE).
