ashfall-main-menu-terminal-title = ASHFALL
ashfall-main-menu-address-placeholder = адрес:порт
ashfall-main-menu-status-connecting = Подключение...

ashfall-lobby-terminal-title = ASHFALL
ashfall-lobby-audio-heading = АУДИОСИСТЕМА
ashfall-lobby-chat-heading = ЧАТ
ashfall-lobby-background-title = Ashfall
ashfall-lobby-background-artist = команда проекта Ashfall
ashfall-lobby-ready-action = Готов к смене
ashfall-lobby-cancel-ready-action = Отменить готовность
ashfall-lobby-join-shift-action = Приступить к смене
ashfall-lobby-observe-action = Режим наблюдения

ashfall-character-setup-registry-title = ASHEN INDUSTRIAL // КАДРОВЫЙ РЕЕСТР
ashfall-character-setup-file-title = ЛИЧНОЕ ДЕЛО СОТРУДНИКА
ashfall-character-setup-status = СТАТУС: ДЕЙСТВУЮЩИЙ
ashfall-character-setup-last-sync = ПОСЛЕДНЯЯ СИНХРОНИЗАЦИЯ: 10 ЛЕТ НАЗАД
ashfall-character-setup-records-heading = ЛИЧНЫЕ ДЕЛА
ashfall-lobby-preview-no-candidate = КАНДИДАТ НЕ ЗАКРЕПЛЁН, ОТКРОЙТЕ ЛИЧНЫЕ ДЕЛА
ashfall-personal-files-slot-move-left = Сдвинуть кандидата влево
ashfall-personal-files-slot-move-right = Сдвинуть кандидата вправо

ashfall-options-title = Настройки
ui-options-log-actions-in-chat = Логировать действия и осмотр в чат
ui-options-log-actions-in-chat-tooltip = Дублирует сообщения об использовании предметов, окружении и осмотре в панель чата.
ui-options-coalesce-identical-messages = Группировать повторяющиеся сообщения в чате
ui-options-coalesce-identical-messages-tooltip = Объединяет одинаковые идущие подряд сообщения в чате со счётчиком.

ashfall-personal-files-title = АРХИВ СОТРУДНИКОВ // КРИОХРАНИЛИЩЕ ASHEN INDUSTRIAL
ashfall-personal-files-subtitle = Выберите сотрудника и подтвердите назначение на смену.
ashfall-personal-files-status = СТАТУС: АКТИВНЫЕ ДЕЛА
ashfall-personal-files-refreshes-left = ОСТАЛОСЬ ОБНОВЛЕНИЙ: { $count }
ashfall-personal-files-refresh = ЗАПРОСИТЬ ДРУГИЕ ЛИЧНЫЕ ДЕЛА
ashfall-personal-files-back = НАЗАД В ЛОББИ
ashfall-personal-files-cooldown = Повторный запрос через { $seconds } с.
ashfall-personal-files-sex-male = Мужской
ashfall-personal-files-sex-female = Женский
ashfall-personal-files-sex-other = Не указан
ashfall-personal-files-card-bio = Возраст: { $age } | Пол: { $sex }
ashfall-personal-files-dossier-bio = Возраст: { $age } | Пол: { $sex } | Вид: { $species }
ashfall-personal-files-dossier-label-age = Возраст
ashfall-personal-files-dossier-label-sex = Пол
ashfall-personal-files-dossier-label-species = Вид
ashfall-personal-files-dossier-label-phenotype = Фенотип
ashfall-personal-files-dossier-label-culture = Культурная линия
ashfall-personal-files-dossier-label-birthplace = Место рождения
ashfall-personal-files-number = ЛИЧНОЕ ДЕЛО #{ $number }
ashfall-personal-files-confirmed-marker = Кандидат подтверждён для пробуждения
ashfall-personal-files-confirmed = ПОДТВЕРЖДЕНО // НАЗНАЧЕНИЕ: { $job }
ashfall-personal-files-assignment-heading = НАЗНАЧЕНИЕ
ashfall-personal-files-assignment-selected = НАЗНАЧЕНИЕ // { $job }
ashfall-personal-files-assignment-help = Доступны только профессии, совместимые с квалификацией сотрудника.
ashfall-personal-files-no-candidate = Сначала выберите сотрудника в архиве.
ashfall-personal-files-no-jobs = Нет доступных назначений. Сотрудник не может быть подтверждён.
ashfall-personal-files-priority-heading = ПРИОРИТЕТЫ ПРОБУЖДЕНИЯ
ashfall-personal-files-priority-help = Выберите сотрудника и должность в блоке «НАЗНАЧЕНИЕ», затем закрепите пару в слоте 1–5; последние два шага можно делать в любом порядке. Слот 1 проверяется первым.
ashfall-personal-files-slot-help = Слот приоритета пробуждения. Нажмите, чтобы закрепить выбранного сотрудника; если должность ещё не выбрана, слот дождётся её выбора.
ashfall-personal-files-slot-empty = пусто
ashfall-personal-files-slot-clear = Убрать из приоритетов
ashfall-personal-files-slot-pending = Слот { $slot } ждёт должность — выберите её в блоке «НАЗНАЧЕНИЕ».
ashfall-personal-files-slot-no-candidate = Сначала выберите сотрудника.
ashfall-personal-files-slot-select-role = Сначала выберите должность в блоке «НАЗНАЧЕНИЕ».
ashfall-personal-files-slot-pinned = Закреплён в слоте { $slot }.
ashfall-personal-files-pinned-marker = Сотрудник закреплён в приоритетах пробуждения
ashfall-personal-files-pinned-label = { $confirmed ->
    [true] ПОДТВЕРЖДЕНО // СЛОТ { $slot } // { $job }
    *[other] ЗАКРЕПЛЁН // СЛОТ { $slot } // { $job }
}
ashfall-personal-files-record-unavailable = ///
ashfall-latejoin-incompatible-job = Эта профессия не соответствует подтверждённому личному делу.
