# InterlacingTwoTexts
BookTextsSplit with backservers

1. Схема взаимодействия классов и методов 

![BackgroundDispatcher_Structure_v-03](https://github.com/YUGCHR/InterlacingTwoTexts/blob/continuation-invisible-embedded-integration-test/BackgroundDispatcher/Diagrams/out_BackgroundDispatcher_Structure/BackgroundDispatcher_Structure_v-031.png)

2. Оописание работы сервера-диспетчера BackgroundDispatcher

основное назначение - собрать одиночные задачи в пакеты и отдать в обработку бэк-серверам BackgroundTasksQueue (серверам фоновой загрузки книг)
особенно полезно собрать минимальный пакет из пары книг - на разных языках, в большинстве случаем именно это будет основным кейсом

дополнительные задачи - 
хранение "вечного" лога исходников загруженных книг с контролем повтора книг
выполнение интеграционных тестов - несколько сценариев и различные глубины выполнения
контроль прохождения рабочих задач через сервер и получение их бэк-сервером
выполнение запроса контроллера о состоянии отправленной на загрузку книги - выполнено или нет и почему
