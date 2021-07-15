using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;
using BooksTextsSplit.Library.Models;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

//
// план работ -
// в конце теста проверять ключи и поля на совпадение (в отдельном методе?)
// отмечать пройденную глубину теста
// сделать нормальное включение ветки теста с третьим ключом
// добавить второй сценарий теста - с шестью событиями
// и сделать больше тестовых ключей с текстами
// можно добавить в конце названий полей приметные номера - например с указанием, что тест, номер книги и язык
// и в ключ тоже что-то добавить (нет, в ключ не надо, он никуда не идёт)
// можно в ключ пакета в кафе добавлять контрольное слово для тестов и потом его находить для определения прохождения теста
// но вообще это всё лишнее - надо чтобы тест сам определял правильность прохождения
// вынести всё (что можно) из главного класса тестов в дополнительные
// 
// те поля, которые не получилось удалить (сами исчезли) надо сложить в отдельный список
// и отдать в специальный метод - он за ними присмотрит
//
// по кругу пока не ходить - в тестах проверить, захватываются ли все вызовы или что-то пропадает
// 

namespace BackgroundDispatcher.Services
{
    public interface ITaskPackageFormationFromPlainText
    {
        public Task<bool> HandlerCallingDistributore(ConstantsSet constantsSet, CancellationToken stoppingToken);
    }

    public class TaskPackageFormationFromPlainText : ITaskPackageFormationFromPlainText
    {
        private readonly ILogger<TaskPackageFormationFromPlainText> _logger;
        private readonly IIntegrationTestService _test;
        private readonly ICacheManageService _cache;

        public TaskPackageFormationFromPlainText(
            ILogger<TaskPackageFormationFromPlainText> logger,
            IIntegrationTestService test,
            ICacheManageService cache)
        {
            _logger = logger;
            _test = test;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TaskPackageFormationFromPlainText>();

        public async Task<bool> HandlerCallingDistributore(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // добавить счётчик потоков и проверить при большом количестве вызовов
            // 

            // получить в строку название метода, чтобы сообщить тесту
            string currentMethodName = FetchCurrentMethodName();
            Logs.Here().Information("{0} started.", currentMethodName);

            // можно добавить задержку для тестирования

            // уже обработанное поле сразу удалить, чтобы не накапливались

            // в случае теста проверяем, достигнута ли глубина тестирования и заодно сообщаем о ходе теста - достигнутой контрольной точки
            // можно перенести отчёт о тестировании в следующий метод и сделать только одну глубину - окончательную
            bool isTestInProgress = _test.IsTestInProgress();
            if (isTestInProgress)
            {
                // сообщаем тесту, что глубина достигнута и проверяем, идти ли дальше
                // если дальше идти не надо, то return прямо здесь
                // передаем в параметрах название метода, чтобы там определили, из какого места вызвали
                // название метода из переменной - currentMethodName
                // инвертировать возврат и переименовать переменную результата в targetDepthReached
                bool targetDepthNotReached = await _test.IsPreassignedDepthReached(constantsSet, "HandlerCallingDistributore", stoppingToken);
                Logs.Here().Information("Test reached HandlerCallingDistributor and will move on - {0}.", targetDepthNotReached);
                if (!targetDepthNotReached)
                {
                    return true;
                }
            }
            // тут еще можно определить, надо ли обновить константы
            // хотя константы лучше проверять дальше
            // тут быстрый вызов без ожидания, чтобы быстрее освободить распределитель для второго потока
            // в тестировании проверить запуск второго потока - и добавить счётчик потоков в обработчик

            _ = HandlerCalling(constantsSet, stoppingToken);

            Logs.Here().Information("{0} is returned true.", currentMethodName);
            return true;
        }

        // можно перенести во вспомогательную библиотеку
        public string FetchCurrentMethodName(bool showLogMethodStarted = false, [CallerMemberName] string currentMethodName = "")
        {
            if (showLogMethodStarted)
            {
                Logs.Here().Debug("{0} started.", currentMethodName);
            }
            return currentMethodName;
        }

        public async Task<int> HandlerCalling(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // обработчик вызовов - что делает (надо переименовать - не вызовов, а событий подписки или как-то так)
            // получает сообщение о сформированном вызове по поводу subscribeOnFrom
            // собирает из subscribeOnFrom все данные и формирует пакет задач с плоским текстом (формирование пакета можно отдать в следующий метод)
            // удаляет обработанные поля subscribeOnFrom
            // проверяет наличие ключа subscribeOnFrom, если остался, ещё раз достаёт поля - и далее по кругу, пока ключ не исчезнет

            // решить, как лучше - ждать, когда ключ исчезнет и только потом формировать пакет задач или формировать новый пакет на каждом круге
            // первый вариант - пакет на каждом круге
            // а если ключ не исчез, подождать стандартные 5 секунд - скорее всего, новые поля заберёт следующие поток

            // вообще, в этом обработчике надо только достать список полей и сразу же удалить поля по списку
            // только успешно удалённые поля будут считаться полученными и пригодными для дальнейшей обработки
            // потом отдать данные в следующий метод

            // те поля, которые не получилось удалить (сами исчезли) надо сложить в отдельный список
            // и отдать в специальный метод - он за ними присмотрит

            // по кругу пока не ходить - в тестах проверить, захватываются ли все вызовы или что-то пропадает

            // вообще-то это всё - разместить пакет задач для бэк-сервера и дальше только контролировать выполнение

            string currentMethodName = FetchCurrentMethodName();
            Logs.Here().Information("{0} started.", currentMethodName);

            // достать ключ и поля (List) плоских текстов из события подписки subscribeOnFrom
            (List<string> fieldsKeyFromDataList, string sourceKeyWithPlainTests) = await ProcessedDataOfSubscribeOnFrom(constantsSet, stoppingToken);

            // ключ пакета задач (новый гуид) и складываем тексты в новый ключ
            string taskPackageGuid = await BackgroundDispatcherCreateTasks(constantsSet, sourceKeyWithPlainTests, fieldsKeyFromDataList);
            // вот тут, если вернётся null, то можно пройти сразу на выход и ничего не создавать - 
            if (taskPackageGuid != null)
            {
                // записываем ключ пакета задач в ключ eventKeyFrontGivesTask
                bool isCafeKeyCreated = await DistributeTaskPackageInCafee(constantsSet, taskPackageGuid);

                if (isCafeKeyCreated) // && test is processing now
                {
                    // вызвать метод теста для сообщения об окончании выполнения
                }
            }
            // никакого возврата никто не ждёт, но на всякий случай вернём ?
            return 0;
        }

        private async Task<(List<string>, string)> ProcessedDataOfSubscribeOnFrom(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // название (назначение) метода - достать ключ и поля плоских текстов из события подписки subscribeOnFrom

            // если тест, надо смотреть тестовый ключ, а не рабочий
            // теперь ключ всегда одинаковый - рабочий
            // убрать все присваивания в отдельный метод, чтобы не путаться (уже одно осталось, нечего убирать)
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            //string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            //string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}"; // subscribeOnFrom:test
            //string eventKey = eventKeyFrom;

            //bool isTestInProgress = _test.IsTestInProgress();
            //if (isTestInProgress)
            //{
            //    eventKey = eventKeyFromTest; // subscribeOnFrom:test
            //}

            IDictionary<string, string> keyFromDataList = await _cache.FetchHashedAllAsync<string>(eventKeyFrom);
            int keyFromDataListCount = keyFromDataList.Count;
            List<string> fieldsKeyFromDataList = new();
            string sourceKeyWithPlainTests = null;

            foreach (var d in keyFromDataList)
            {
                (var f, var v) = d;
                Logs.Here().Information("Dictionary element is {@F} {@V}.", new { Filed = f }, new { Value = v });

                // удаляем текущее поле (для точности и скорости перед удалением можно проверить существование? и, если есть, то удалять)
                bool isFieldRemovedSuccessful = await _cache.DelFieldAsync(eventKeyFrom, f);
                Logs.Here().Information("{@F} in {@K} was removed with result {0}.", new { Filed = f }, new { Key = eventKeyFrom });

                // если не удалилось - и фиг с ним, удаляем его из словаря
                // можно убрать, всё равно словарь больше не используется
                //if (!isFieldRemovedSuccessful)
                //{
                //    keyFromDataList.Remove(f);
                //    // а вот не фиг - тут сохраняем его куда-то (потом)
                //}
                // если удалилось, то переписываем в лист
                // кстати, из словаря тогда можно не удалять - он уже никому неинтересен
                // и инвертировать логику и без else
                if (isFieldRemovedSuccessful)
                {
                    fieldsKeyFromDataList.Add(f);
                    // можно каждый раз проверять, что ключ одинаковый - если больше нечего делать
                    sourceKeyWithPlainTests = v;
                    Logs.Here().Information("Future {@K} with {@F} with plain text.", new { Key = v }, new { Filed = f });
                }
            }

            int fieldsKeyFromDataListCount = fieldsKeyFromDataList.Count;
            Logs.Here().Information("{0} tasks were found, {1} tasks were proceeded.", keyFromDataListCount, fieldsKeyFromDataListCount);

            return (fieldsKeyFromDataList, sourceKeyWithPlainTests);
        }

        private async Task<string> BackgroundDispatcherCreateTasks(ConstantsSet constantsSet, string sourceKeyWithPlainTexts, List<string> taskPackageFileds)
        {
            // получили ключ-гуид и список полей, по сути, это уже готовый пакет
            // сам ключ уже сформирован и ждёт - можно получить плоские тесты
            // похоже, ключ лучше бы заменить - потому что бэк-сервер будет вычерпывать ключ весь
            // а он там постоянный на все время сессии BooksTextsSplit
            // а уникальный ключ текущего запроса за сохранение книги - он в поле
            // надо уточнить, как там с языковой парой - у них одинаковый ключ или разный
            // каждая книга заезжает отдельно, не парой и имеет уникальный гуид, созданный контроллером в момент отправки книги
            // ключ общий у всех книг, можно было бы заменить его на уникальный
            // но это всё равно ничего не даёт - книги (тексты) придётся переписывать в пакет задач в любом варианте
            // так что получили не готовый пакет, а только заготовку
            // план действий метода -
            // генерируем новый гуид - это будет ключ пакета задач
            // достаём по одному тексты и складываем в новый ключ
            // гуид пакета отдаём в следующий метод

            if (sourceKeyWithPlainTexts == null)
            {
                _test.SomethingWentWrong(false);
                return null;
            }

            string taskPackage = constantsSet.Prefix.BackgroundDispatcherPrefix.TaskPackage.Value; // taskPackage
            double taskPackageGuidLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.TaskPackage.LifeTime; // 0.001
            string currentPackageGuid = Guid.NewGuid().ToString();
            string taskPackageGuid = $"{taskPackage}:{currentPackageGuid}"; // taskPackage:guid

            //List<bool> resultPlainText = new();
            int inPackageTaskCount = 0;

            foreach (var f in taskPackageFileds)
            {
                // прочитать первое поле хранилища
                TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(sourceKeyWithPlainTexts, f);
                Logs.Here().Information("Test plain text was read from key-storage");

                // вот тут самый подходящий момент посчитать хэш
                // создать новую версию через хэш и записать её в плоский текст
                // всё равно читаем его и заново пишем, момент просто создан для вмешательства

                bookPlainText = await AddVersionViaHashToPlainText(constantsSet, bookPlainText);
                Logs.Here().Information("Hash version was added to {@B}.", new { BookPlainText = bookPlainText });

                // может вернуться null, надо придумать, что с ним делать - это означает, что такой текст есть и работать с ним не надо
                // не проверяется второй текст и, очевидно, всё следующие в пакете
                // возвращать null не надо, просто не будем записывать - и создавать поле задачи в пакете задач

                if (bookPlainText != null)
                {
                    // перенести инкремент счётчика под if
                    inPackageTaskCount++;
                    Logs.Here().Information("Hash version was added {0} time to {@B}.", inPackageTaskCount, new { BookPlainText = bookPlainText });

                    // создать поле плоского текста
                    await _cache.WriteHashedAsync<TextSentence>(taskPackageGuid, f, bookPlainText, taskPackageGuidLifeTime);
                    Logs.Here().Information("Plain text {@F} No. {0} was created in {@K}.", new { Filed = f }, inPackageTaskCount, new { Key = taskPackageGuid });
                }
            }

            if (inPackageTaskCount == 0)
            {
                // в конце, при возврате taskPackageGuid проверять счётчик
                // если ничего не насчитал, то возвратить null
                Logs.Here().Information("Hash version was added in 0 cases.");
                return null;
            }

            return taskPackageGuid;
        }

        private async Task<TextSentence> AddVersionViaHashToPlainText(ConstantsSet constantsSet, TextSentence bookPlainText)
        {
            // обратиться к тестам и пусть они посчитают хэш и сообщат, есть ли уже такой в списке и, если нет, присвоят номер версии
            // хэш лучше посчитать прямо здесь и передавать его для экономии памяти, кроме него нужен номер книги и язык
            // с этим тоже проблема, особо взять их негде, кроме как из фронт-веба, а это ненадёжные данные, но пока будем брать оттуда

            // ключ, в котором хранятся все хэши - keyBookPlainTextsHashesVersionsList
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value;

            int bookId = bookPlainText.BookId;
            int languageId = bookPlainText.LanguageId;
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
            // создаём поле с номером книги и языком книги, если англ, то поле просто номер, а если рус, то поле миллион номер
            int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;

            string bookPlainTextMD5Hash = CreateMD5(bookPlainText.BookPlainText);

            // отдать методу хэш, номер и язык книги, получить номер версии, если такой хэш уже есть, то что вернуть? можно -1
            int versionHash = await ChechPlainTextVersionViaHash(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bookPlainTextMD5Hash);
            Logs.Here().Information("Hash version {0} was returned.", versionHash);

            // получили -1, то есть, такой текст уже есть, возвращаем null, там разберутся
            if (versionHash < 0)
            {
                Logs.Here().Warning("This plain text already exist. {@B} / {@L}.", new { BookId = bookId }, new { LanguageId = languageId });
                return null;
            }

            // получили 0, надо создать лист и первый элемент в нём и положить в ключ новое поле (возможно и сам ключ создать, но это неважно)
            if (versionHash == 0)
            {
                List<TextSentence> bookPlainTextsHash = new List<TextSentence>();
                // положить первый элемент - заглушку, иначе CachingFramework.Redis сохраняет не List с одним элементом, а просто один элемент (!?)
                TextSentence bookPlainTextHash = new TextSentence()
                {
                    BookId = bookPlainText.BookId,
                    LanguageId = bookPlainText.LanguageId,
                    HashVersion = 0,
                    BookPlainTextHash = "00000000000000000000000000000000",
                    BookPlainText = null
                };

                bookPlainText = await WriteBookPlainTextHash(constantsSet, bookPlainText, bookPlainTextsHash, versionHash, bookPlainTextMD5Hash);
                return bookPlainText;
            }

            // получили номер версии, лист уже есть, надо его достать, создать новый элемент, добавить в лист и записать в то же место
            if (versionHash > 0)
            {
                List<TextSentence> bookPlainTextsHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);
                bookPlainText = await WriteBookPlainTextHash(constantsSet, bookPlainText, bookPlainTextsHash, versionHash, bookPlainTextMD5Hash);
                return bookPlainText;
            }
            return default;
        }



        // не проверяется второй текст и, очевидно, всё следующие в пакете




        // метод создаёт элемент List-хранилища хэшей плоских текстов и обновляет сам плоский текст, добавляя в него хэш и версию текста
        private async Task<TextSentence> WriteBookPlainTextHash(ConstantsSet constantsSet, TextSentence bookPlainText, List<TextSentence> bookPlainTextsHash, int versionHash, string bookPlainTextMD5Hash)
        {
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value;
            double keyBookPlainTextsHashesVersionsListLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.LifeTime;

            int bookId = bookPlainText.BookId;
            int languageId = bookPlainText.LanguageId;
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
            // создаём поле с номером книги и языком книги, если англ, то поле просто номер, а если рус, то поле миллион номер
            int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;

            TextSentence bookPlainTextHash0 = new TextSentence()
            {
                BookId = bookPlainText.BookId,
                LanguageId = bookPlainText.LanguageId,
                HashVersion = versionHash + 1,
                BookPlainTextHash = bookPlainTextMD5Hash,
                BookPlainText = null
            };

            bookPlainTextsHash.Add(bookPlainTextHash0);

            bookPlainText.HashVersion = versionHash + 1;
            bookPlainText.BookPlainTextHash = bookPlainTextMD5Hash;

            int bookHashVersion = bookPlainText.HashVersion;
            string bookPlainTextHash = bookPlainText.BookPlainTextHash;

            // новый вариант bookPlainTexttHash без текста, только с хэшем будет существовать в ключе со списком всех загруженных книг
            await _cache.WriteHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bookPlainTextsHash, keyBookPlainTextsHashesVersionsListLifeTime);

            // тут проверяем и показываем как записались версия и хэш в существующий bookPlainText и его же (обновлённый) возвращаем из метода
            Logs.Here().Information("New book {@B} plain text {@L} hash and version were updated. {@V} \n {@H}.", new { BookId = bookId }, new { LanguageId = languageId }, new { HashVersion = bookHashVersion }, new { BookPlainTextHash = bookPlainTextHash });
            return bookPlainText;
        }


        // метод проверяет существование хэша в хранилище хэшей плоских текстов, может вернуть -
        // -1, если такой хэш есть
        // 0, если такого поля/ключа вообще нет, записывать надо первую версию
        // int последней существующей версии, записывать надо на 1 больше
        private async Task<int> ChechPlainTextVersionViaHash(string keyBookPlainTextsHashesVersionsList, int fieldBookIdWithLanguageId, string bookPlainTextMD5Hash)
        {
            int maxVersion = 0;

            // 1 проверить существование ключа вообще и полученного поля (это уже только его чтением)
            bool soughtKey = await _cache.IsKeyExist(keyBookPlainTextsHashesVersionsList); // искомый ключ
            Logs.Here().Information("{@K} existing is {0}.", new { Key = keyBookPlainTextsHashesVersionsList }, soughtKey);

            if (!soughtKey)
            {
                // _test.SomethingWentWrong(!soughtKey);
                // если ключа вообще нет, тоже возвращаем 0, будет первая книга с первой версией в ключе
                Logs.Here().Information("{@K} existing is {0}, 0 is returned.", new { Key = keyBookPlainTextsHashesVersionsList }, soughtKey);
                return 0;
            }

            List<TextSentence> bookPlainTextsVersions = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);

            // 2 если поля нет, возвращаем результат - первая версия 
            if (bookPlainTextsVersions == null)
            {
                Logs.Here().Information("{@F} is not existed - {@B}, 0 is returned.", new { Field = fieldBookIdWithLanguageId }, new { Books = bookPlainTextsVersions });
                return 0;
            }
            // 3 если поле есть, достаём из него значение - это лист
            // 4 перебираем лист, достаём хэши и сравниваем с полученным в параметрах
            foreach (TextSentence v in bookPlainTextsVersions)
            {
                // одновременно с этим находим максимальную версию, сохраняем в maxVersion
                int hashVersion = v.HashVersion;
                Logs.Here().Information("{@B} is existed, version {0} was fetched.", new { Books = bookPlainTextsVersions }, hashVersion);

                if (hashVersion > maxVersion)
                {
                    Logs.Here().Information("New Max version will be {0}, was {1}.", hashVersion, maxVersion);
                    maxVersion = hashVersion;
                }

                // 5 если совпадение нашлось, возвращаем отлуп - пока -1
                string bookPlainTextHash = v.BookPlainTextHash;
                bool isThisHashExisted = String.Equals(bookPlainTextHash, bookPlainTextMD5Hash);
                Logs.Here().Information("{@H} and {@M}, String.Equals is {0}.", new { SavedHash = hashVersion }, new { TesteeHash = bookPlainTextMD5Hash }, isThisHashExisted); // испытуемый

                if (isThisHashExisted)
                {
                    Logs.Here().Information("The same hash was found {0} in the storage, -1 will be returned.", isThisHashExisted);
                    return -1;
                }
            }
            // 6 если совпадения нет, берём maxVersion, прибавляем 1 и возвращаем версию
            // прибавлять 1 будем там, где будем записывать новый хэш

            Logs.Here().Information("Testee hash is inique, Max version of the book is {0} and it will be returned.", maxVersion);

            return maxVersion;


            // 7 записывать будем в другом месте, потому что тут нет самого текста
            // (хотя он и не нужен, можно и тут записать)
            // всё же тут не пишем - нечего писать, есть только поле с номером и языком книги
            // с другой стороны, надо хранить номер, язык и версию книги, а также хэш текста, а это всё здесь есть
            // лучше бы конечно взять исходный плоский текст и сам текст удалить, оставив всё остальное описание
            // попробуем писать не тут

        }

        // метод из анализа книги
        public string GetMd5Hash(string fileContent)
        {
            MD5 md5Hasher = MD5.Create(); //создаем объект класса MD5 - он создается не через new, а вызовом метода Create            
            byte[] data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(fileContent));//преобразуем входную строку в массив байт и вычисляем хэш
            StringBuilder sBuilder = new StringBuilder();//создаем новый Stringbuilder (изменяемую строку) для набора байт
            for (int i = 0; i < data.Length; i++)// Преобразуем каждый байт хэша в шестнадцатеричную строку
            {
                sBuilder.Append(data[i].ToString("x2"));//указывает, что нужно преобразовать элемент в шестнадцатиричную строку длиной в два символа
            }
            string pasHash = sBuilder.ToString();

            return pasHash;
        }

        public static string CreateMD5(string input)
        { // https://stackoverflow.com/questions/11454004/calculate-a-md5-hash-from-a-string
            // Use input string to calculate MD5 hash
            MD5 md5 = MD5.Create();

            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Convert the byte array to hexadecimal string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private async Task<bool> DistributeTaskPackageInCafee(ConstantsSet constantsSet, string taskPackageGuid)
        {
            // только после того, как создан ключ с пакетом задач, можно положить этот ключ в подписной ключ eventKeyFrontGivesTask
            // записываем ключ пакета задач в ключ eventKeyFrontGivesTask, а в поле и в значение - ключ пакета задач
            // сервера подписаны на ключ eventKeyFrontGivesTask и пойдут забирать задачи, на этом тут всё
            // сделать подписку на ключ кафе и по событию пакет в кафе сообщать тестам -
            // вызывать финальный метод проверки результатов теста (не отсюда вызывать, а по подписке)
            // в дальнейшем будет частью постоянной самопроверки
            // возвращать true из метода DistributeTaskPackageInCafee только после проверки этим постоянным тестом
            // только надо как-то узнать, какие результаты у этого теста - можно вызвать метод из класса теста, который проверит какой-нибудь флаг
            // или более приземлённый способ с проверкой ключа (с флагом выглядит лучше - там сразу можно и подождать положенное время)

            // и подписка на ключ кафе - дело шаткое, бэк-сервер быстро схватит этот ключ и удалит
            // надо или делать задержку на сервере или вызывать тест не по подписке
            // точнее, сначала не по подписке, а потом проконтролировать появление и исчезновение ключа кафе
            // ну и далее регистрацию пакета на бэк-сервере

            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package
            double cafeKeyLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.LifeTime;

            await _cache.WriteHashedAsync(cafeKey, taskPackageGuid, taskPackageGuid, cafeKeyLifeTime);
            Logs.Here().Information("{@T} was placed in {@C}.", new { Task = taskPackageGuid }, new { Cafe = cafeKey });

            return true;
        }
    }
}
