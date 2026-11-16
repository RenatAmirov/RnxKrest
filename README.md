# Крест

Настольная головоломка для Windows по мотивам [«Креста» Кирилла Заборского](https://pcwin.ru/golovolomka-krest_download/). Механика сдвига обойм восстановлена по рисункам и описанию [«Креста 2×2» у В. Н. Дубровского и А. Т. Калинина](https://www.mathedu.ru/text/dubrovskiy_kalinin_matematicheskie_golovolomki_1990/p123/).

## Как играть

Совместите фишки с образцом справа. Каждая стрелка сдвигает горизонтальную или вертикальную обойму на одну клетку. Со сдвигаемой обоймой перемещаются восемь фишек, включая четыре в общей области.

| Действие | Управление |
| --- | --- |
| Сдвиг на клетку | Стрелка на клавиатуре или кнопка на поле |
| Сдвиг до упора | Shift и стрелка или Shift и щелчок по кнопке |
| Следующий ход по кратчайшему решению | H |
| Отменить ход | Ctrl+Z |
| Новая партия | Ctrl+N |
| Правила | F1 |

## Запуск и сборка

В готовой сборке для Windows x64 запустите `publish/RnxKrest.exe`.

Для запуска из исходников нужен .NET 10 SDK.

```powershell
dotnet run --project RnxKrest.csproj
```

### Самодостаточная сборка для Windows x64

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```
