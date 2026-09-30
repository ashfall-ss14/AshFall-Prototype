<p align="center">
  <img width="1536" height="384" alt="AshFallLogo" src="Resources/Textures/Ashfall/AshFallLogo.jpg" />
</p>

<p align="center">
  <a href="https://discord.gg/6hZhXhtUv"><img src="https://img.shields.io/badge/Discord-Присоединиться-5865F2?style=flat-square&logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="https://github.com/space-wizards/space-station-14"><img src="https://img.shields.io/badge/База-Space_Station_14-44403c?style=flat-square" alt="Space Station 14" /></a>
  <a href="#лицензия"><img src="https://img.shields.io/badge/Лицензия-MIT_%2F_AGPLv3-ea580c?style=flat-square" alt="Лицензия" /></a>
</p>

<p align="center">
  <a href="#об-игре">Об игре</a> •
  <a href="#механики">Механики</a> •
  <a href="#как-играть">Как играть</a> •
  <a href="#сборка">Сборка</a> •
  <a href="#лицензия">Лицензия</a> •
  <a href="https://discord.gg/6hZhXhtUv">Discord</a>
</p>

---

## Об игре

**ASHFALL** - русскоязычный High RP сервер на базе Space Station 14 с оглядкой на Orbital Station 13.

2291 год. Корпорация Ashen Industrial обанкротилась, оставив станцию без снабжения и связи с внешним миром. Десять лет она медленно угасала, пока неисправная автоматика не начала аварийно выводить персонал из криосна.

Теперь выжившим предстоит удержать станцию на плаву и вернуть её к жизни. Припасы на исходе, а помощи ждать неоткуда.

## Механики

- **Тотальный износ:** десять лет без обслуживания уничтожили станцию. Ломается буквально всё: от систем жизнеобеспечения и обшивки корпуса до личной экипировки и оружия, которое из-за ржавчины может разорвать прямо в руках при выстреле.
- **Процедурная станция:** каждый раунд генерирует новую планировку комнат, уникальный набор критических поломок и глобальные задачи - от консервации реактора до сборки аварийного SOS-маяка.
- **Сквозная прогрессия экипажа:** персонажи, пережившие смену, переходят в следующие раунды со всем накопленным опытом и травмами. А вот гибель - окончательна: в случае смерти герой выбывает навсегда, хотя каждая смена остаётся завершённой историей.
- **Случайные досье:** перед сменой игрок выбирает одного из сгенерированных сотрудников криобазы с готовой биографией, возрастом, рабочей специальностью и одной из 18 культур.
- **Знакомые лица:** при осмотре других членов экипажа через Shift+Click всплывают детали общего прошлого - от давней службы до забытого долга в 250 кредитов.
- **Автономное снабжение:** классический карго-отдел и бюджет отсутствуют. Все поставки завязаны на утилизацию: экипаж захватывает тяговым лучом обломки в открытом космосе и вручную распиливает их на сталь и другие компоненты.

## Как играть

Проект в разработке, плейтесты анонсируются в сообществе.

1. Установите официальный лаунчер [Space Station 14](https://spacestation14.io/) или альтернативный [Trauma Launcher](https://github.com/Trauma-Station/Trauma.Launcher).
2. Зайдите в наш [Discord](https://discord.gg/6hZhXhtUv) за расписанием тестов и адресом сервера.

## Сборка

Требуются Git, [.NET 9.0 SDK](https://dotnet.microsoft.com/) и Python 3.10+.

```bash
git clone https://github.com/he1acdvv/AshFall-Prototype.git
cd AshFall-Prototype
python RUN_THIS.py
dotnet build
```

Запуск: `./runclient.sh` и `./runserver.sh` (для Windows - `.bat` файлы в корне).

Правила участия в разработке описаны в [CONTRIBUTING.md](CONTRIBUTING.md) и [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Лицензия

- Исходный код Space Station 14: [LICENSE.TXT](LICENSE.TXT) (MIT).
- Код Ashfall: [PROJECT-LICENSE.md](PROJECT-LICENSE.md).
- Доноры и апстримы: [UPSTREAM.md](UPSTREAM.md) и [DONORS.yml](DONORS.yml).
- Контакты: [SECURITY.md](SECURITY.md) или [Discord](https://discord.gg/6hZhXhtUv).
