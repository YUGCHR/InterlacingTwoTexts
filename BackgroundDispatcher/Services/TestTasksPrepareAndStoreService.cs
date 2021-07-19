﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BooksTextsSplit.Library.Models;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

#region TestTasksPrepareAndStoreService description

// 0, 1 и 2 убираем, можно только 10-12, 20-22 и так далее, номеров тестовых книг вряд ли будет сильно больше десятка
// потом можно добавить, что 0 - отметка начала повтора, 1 - конец и потом идёт число повторов
// но тестовых книг не так много, чтобы так расходиться, оставим на (далёкое) будущее
// отрицательное число в ряду - задержка в миллисекундах, зная задержку таймера из констант,
// можно подбираться ближе к срабатыванию с разных сторон, добиваясь сдвоенного срабатывания (или нет)

// можно сразу в одном ряду проверять несколько задержек, скажем -
// 12, 22, -4700, 32, -1000,
// 12, 22, -4800, 32, -1000,
// 12, 22, -4900, 32, -1000

// будет означать, что взять пару книг с номером 73 (к примеру) первой версии, потом пару 75 и, после задержки 4,7 сек, пару 77, потом на всякий случай секунду (и ещё вариант без неё) и повторить с другой версией и другой задержкой
// как тогда сделать повтор
// можно перейти к трёхзначным числам и последняя цифра будет версией
// если версий/номеров не хватает, ряд (сценарий) заканчивается с оповещением, чего не хватило

// тогда такой вид -
// 121, 221, -4700, 321, -1000,
// 122, 222, -4800, 322, -1000,
// 123, 223, -4900, 323, -1000
// три пары книг по три разных версии каждой

// если в версии 0, это может означать взять следующую версию автоматически
// аналогично, с номерами книг -
// 2, 2, -4700, 2, -1000,
// 2, 2, -4800, 2, -1000,
// 2, 2, -4900, 2, -1000
// может означать, что каждый раз брать новую пару книг, пока не кончатся, потом брать следующие версии

// и после теста надо же проверить ключи кафе и понять, соответствуют ли они заявленным требованиям

// тогда параметр сценария может быть либо массивом (списком) целых чисел, либо ключом, в котором поле это индекс массива, а в значении значение
// ключ выглядит более заманчиво, особенно, когда его надо будет передать из веб-интерфейса

// порядок действий примерно такой -
// смотрим первый элемент/значение, если пара, берём обе книги первой версии,
// первого номера (или случайного, как обозначить?), соответственно,
// если одна, то берём одну из, вторую вычёркиваем, не будем вообще использовать
// смотрим следующий индекс - берём или следующую книгу или следующую версию этой же

// тогда создаём список гуид-полей следующим образом -
// берём номер книги (для определённости скажем случайный и книги парами, одиночные тоже понятно как),
// достаём оба списка версий из номерных полей пары,
// берём первую (с нулевым индексом - или лучше последнюю?) версию,
// достаём из списка гуид-поле текста книги (обоих книг) и складываем в выходной список по порядку

// второй такой же список, только int, делаем с задержками, потом когда будем считывать список книг для генерации событий,
// параллельно будем смотреть в список задержек с таким же индексом, 0 - нет задержки и так далее

// пустышки не удаляем ни в коем случае, ни сейчас, ни раньше - когда в отдельно взятом списке останется только пустышка, просто удалим это поле

// после того, как будут готовы выходные списки - один с гуид-полями, другой с задержками, надо удалить всё лишнее -

// 1 все тестовые поля из вечного лога, теперь не надо что-то оставлять,
// чтобы посмотреть на наложение (повтор книги) - это можно предусмотреть в сценарии

// 2 удалить образовавшийся пакет задач
// (хотя можно и оставить для сравнения, отредактировав его в соответствии с выходным списком - просто удалив ненужные поля)
// нет, возиться с пакетом задач нет смысла, просто удалить - пакет всё же лучше не удалять,
// а привести в нужное состояние и использовать для сравнения
// хотя там есть вычисленные хэши и версии хэшей, надо ещё подумать над его использованием

// кстати, метод, который за контроллером пишет ключ с книгой, а потом ключ с оповещением,
// надо вынести в общую библиотеку и потом его же использовать в тесте
// (там, наверное, стоит регулируемая задержка между записями или проверка записи книги) -
// чтобы тест точно совпадал с рабочим вариантом

// и надо при проверке результатов не пытаться состязаться с серверами в чтении ключа кафе,
// а тихо и спокойно получить ключ пакета задач до создания ключа кафе - метод, создающий ключ кафе перед его записью может проверить,
// не тест ли сейчас и сразу передать ключ в эту проверку (или в отдельный следующий вызов) 

// а за ключом кафе, как и предполагалось, просто смотреть со стороны - как его едят

// CreateTestScenarioKey - временный метод, создающий ключ, в котором хранится последовательность тестового сценария,
// потом переделать его в генерацию ключа из веба
// 

// CreateTestScenarioLists




#endregion

namespace BackgroundDispatcher.Services
{
    public interface ITestTasksPrepareAndStoreService
    {
        public Task<(string, int)> CreateTestScenarioKey(ConstantsSet constantsSet, int testScenario);
        public Task<string> CreateTaskPackageAndSaveLog(ConstantsSet constantsSet, string sourceKeyWithPlainTexts, List<string> taskPackageFileds);
        public Task<List<int>> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0);
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "");
        public Task<bool> RemoveWorkKeyOnStart(string key);

    }

    public class TestTasksPrepareAndStoreService : ITestTasksPrepareAndStoreService
    {
        private readonly ICacheManageService _cache;

        public TestTasksPrepareAndStoreService(ICacheManageService cache)
        {
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestTasksPrepareAndStoreService>();

        //private bool _isTestInProgress;

        // во время теста (при старте) -
        // проверить наличие ключа-хранилища тестовых задач
        // если ключа нет, то и тест надо прекратить (может, в зависимости от сценария)

        // проверить наличие ключа созданных описаний тестовых задач
        // если ключа нет, надо его создать -
        // считать всё из ключа-хранилища
        // прочитать у каждого поля номер книги и прочее
        // записать в ключ описаний листом или по полям, поля как в ключе хранения лога - номера книг и со смещением признак языка

        // потом перенести в BooksTextsSplit.Library в ControllerDataManager (например)
        public async Task<(string, int)> CreateTestScenarioKey(ConstantsSet constantsSet, int testScenario)
        {
            string testScenarioSequenceKey = constantsSet.Prefix.IntegrationTestPrefix.TestScenarioSequenceKey.Value; // test-scenario-sequence
            double testScenarioSequenceKeyLifeTime = constantsSet.Prefix.IntegrationTestPrefix.TestScenarioSequenceKey.LifeTime; // 0.001

            // Scenario 1
            int[] scenario1 = new int[] { 2, 2, -3700 };

            // Scenario 2
            int[] scenario2 = new int[] { 121, 221, -4500, 321 };

            // Scenario 3
            int[] scenario3 = new int[] { 121, 221, -4700, 321, -1000, 122, 222, -4800, 322, -1000, 123, 223, -4900, 323, -1000 };

            int[] selectedScenario = SwitchArraySelect(testScenario, scenario1, scenario2, scenario3);
            Logs.Here().Information("Scenario {0} was selected - {@S}", testScenario, new { ScenarioSequence = selectedScenario });

            bool testSettingKey1WasDeleted = await RemoveWorkKeyOnStart(testScenarioSequenceKey);

            // проверить, есть ли в подключенной реализации и попробовать использовать
            // public async Task SetHashedAsync<TK, TV>(string key, IEnumerable<KeyValuePair<TK, TV>> fieldValues, TimeSpan? ttl = null, ...)

            //for (int i = 0; i < selectedScenario.Length; i++)
            //{
            //    await _cache.WriteHashedAsync<int, int>(testScenarioSequenceKey, i, selectedScenario[i], testScenarioSequenceKeyLifeTime);
            //}

            IDictionary<int, int> fieldValues = new Dictionary<int, int>();

            for (int i = 0; i < selectedScenario.Length; i++)
            {
                fieldValues.Add(i, selectedScenario[i]);
            }

            await _cache.WriteHashedAsync<int, int>(testScenarioSequenceKey, fieldValues, testScenarioSequenceKeyLifeTime);

            IDictionary<int, int> fieldValuesResult = await _cache.FetchHashedAllAsync<int, int>(testScenarioSequenceKey);

            foreach (var p in fieldValuesResult)
            {
                (int i, int v) = p;
                if (v != selectedScenario[i])
                {
                    Logs.Here().Error("Scenario creation was failed - {0} != {1}", v, selectedScenario[i]);
                    return (null, 0);
                }
            }
            // на самом деле возвращать нечего и некому, так как метод будет в ControllerDataManager и общаться с сервером только через ключ
            return (testScenarioSequenceKey, selectedScenario.Length);
        }

        private static int[] SwitchArraySelect(int testScenario, int[] scenario1, int[] scenario2, int[] scenario3) => testScenario switch
        {
            1 => scenario1,
            2 => scenario2,
            3 => scenario3,
            _ => throw new ArgumentOutOfRangeException(nameof(testScenario), $"Not expected direction value: {testScenario}"),
        };

        // 
        public async Task<string> CreateTestScenarioLists(ConstantsSet constantsSet, List<int> uniqueBookIdsFromStorageKey)
        {
            string testScenarioSequenceKey = constantsSet.Prefix.IntegrationTestPrefix.TestScenarioSequenceKey.Value; // test-scenario-sequence
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
            // ключ, в котором хранятся все хэши - keyBookPlainTextsHashesVersionsList
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list

            int uniqueBookIdsFromStorageKeyCount = uniqueBookIdsFromStorageKey.Count;

            List<int> delayList = new();

            IDictionary<int, int> fieldValuesResult = await _cache.FetchHashedAllAsync<int, int>(testScenarioSequenceKey);
            int fieldValuesResultCount = fieldValuesResult.Count;

            for (int i = 0; i < fieldValuesResultCount; i++)
            {
                int sequenceCell = fieldValuesResult[i];
                Logs.Here().Information("Scenario cell {0} was fetched on step {1}", sequenceCell, i);

                // меньше десяти (автоматический выбор)

                if (sequenceCell > 100)
                {
                    // больше ста, варианты -
                    // сотни - номер книги по порядку от случайного отсчёта - разделить на сто, остаток разделить на 10
                    int hundreds = sequenceCell / 100;
                    int hundredsRemainder = sequenceCell % 100;

                    // десятки - варианты - 0, 1 и 2 - языки (2 - оба)
                    int tens = hundredsRemainder / 10;

                    // единицы - номер версии по порядку с начала(или с конца, без разницы)
                    int tensRemainder = hundredsRemainder % 10;
                    // лучше с конца, пока не упрётся в пустышку
                    Logs.Here().Information("Scenario Cell consists of {@B}, {@L}, {@V}", new { SequentialBookId = hundreds }, new { LanguageIdSelection = tens }, new { SequentialVersion = tensRemainder });

                    // смотрим первый элемент/значение, если пара, берём обе книги первой версии,
                    // первого номера (или случайного, как обозначить?), соответственно,
                    // если одна, то берём одну из, вторую вычёркиваем, не будем вообще использовать
                    // смотрим следующий индекс - берём или следующую книгу или следующую версию этой же
                    // будем считать, что количество сотен, это номер книги по порядку из списка (если столько в списке нет, нормируем или ?)

                    bool outOfBookIdsIndex = hundreds > uniqueBookIdsFromStorageKeyCount;
                    if (outOfBookIdsIndex)
                    {
                        SomethingWentWrong(outOfBookIdsIndex);
                    }

                    // 2 - это пара книг на двух языках, остальные варианты пока не рассматриваем, а то вообще никогда не написать
                    if (tens == 2)
                    {
                        // номера сотен идут с единицы, а индексы списка с нуля
                        int engBookId = uniqueBookIdsFromStorageKey[hundreds - 1]; 
                        int rusBookId = engBookId + chapterFieldsShiftFactor;
                        
                        // достать оба поля из вечного лога
                        List<TextSentence> engPlainTextHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, engBookId);
                        List<TextSentence> rusPlainTextHash = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, rusBookId);

                        // теперь версия - они тоже нумеруются с единицы, но нулевой элемент - пустышка, поэтому единицу не вычитаем 
                        int engPlainTextHashCount = engPlainTextHash.Count;
                        bool outOfTextHashIndex = tensRemainder > engPlainTextHashCount;
                        if (outOfTextHashIndex)
                        {
                            SomethingWentWrong(outOfTextHashIndex);
                        }

                        // можно сделать цикл на два прохода и уменьшить количество переменных, правда список станет двумерным
                        TextSentence engPlainTextHashVersion = engPlainTextHash[tensRemainder];
                        TextSentence rusPlainTextHashVersion = rusPlainTextHash[tensRemainder];

                        // теперь надо достать из текст значение полей из которых они родом - и это конечный результат,
                        // можно идти за текстами в тестовое хранилище

                        // создать ключ/поле из префикса и гуид книги
                        string engBookFieldGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{engPlainTextHashVersion.BookGuid}";
                        string rusBookFieldGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{rusPlainTextHashVersion.BookGuid}";

                        Logs.Here().Information("English book field-guid = {0}, rus = {1}", engBookFieldGuid, rusBookFieldGuid);

                        // можно выходные данные сделать а виде словаря - ключ это гуид-поле, а значение - это int задержка после этого поля

                    }

                }

                // вариант - меньше нуля (задержка), выравнивание хромает - задержка получается без ключа, надо смотреть на момент применения
                if (sequenceCell < 0)
                {
                    delayList.Add(Math.Abs(sequenceCell));
                }
                else
                {
                    delayList.Add(0);
                }

            }
        }


        public async Task<string> PrepareTestBookIdsListFromEternalLog(ConstantsSet constantsSet, string sourceKeyWithPlainTexts)
        {





        }



        #region CreateTaskPackageAndSaveLog for FormTaskPackageFromPlainText (via IntegrationTestService)

        // метод создаёт тестовые плоские тексты для тестов
        // берет нужное количество книг/версий для сценария из хранилища
        // и из них делает ключи, неотличимые от приходящих из веб-интерфейса
        // если сценарий предусматривает, то по окончанию теста новые ключи с хэшами должны быть удалены из хранилища хэшей-версий
        public async Task<string> CreateTaskPackageAndSaveLog(ConstantsSet constantsSet, string sourceKeyWithPlainTexts, List<string> taskPackageFileds)
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
                SomethingWentWrong(false);
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

                // перенести AddVersionViaHashToPlainText в тесты и он и там принесёт пользу
                // только можно сразу в следующий класс - типа, подготовка тестовых задач и учёт логов
                // выполняемые функции класса -
                // работа с набором тестовых задач, их хранение, составление оглавления и выдача по требованию
                // составление и хранение описания реальных задач (хэш без текста)
                // проверка реальных задач на повторение
                // и можно не хранить отдельно список-оглавление тестовых задач, а пусть живут в общем списке
                // можно отделять по двузначным номерам книг - реальные будет иметь больше знаков (3-5 ?)
                // тогда, если нужна пара тестовых книг, можно выбрать из списка полей... это уже неудобно - когда-то их станет очень много
                // можно ещё хранить номера тестовых книг в константах, их там всего 5 штук
                // но всё равно идея правильная - реальные задачи будут с 3+значными номерами
                // в тесты можно добавить константу - число, меньше которого тестовые номера книг
                // тогда ещё добавить оригинальное гуид-поле книги в хранимый хэш и будет легко доставать тестовые тексты
                // можно добавлять эти поля только если тестовая книга - чтобы зря не увеличивать объём
                bookPlainText = await AddVersionViaHashToPlainText(constantsSet, bookPlainText);
                // может вернуться null, надо придумать, что с ним делать - это означает, что такой текст есть и работать с ним не надо
                // не проверяется второй текст и, очевидно, всё следующие в пакете
                // возвращать null не надо, просто не будем записывать - и создавать поле задачи в пакете задач

                if (bookPlainText != null)
                {
                    Logs.Here().Information("Hash version was added to {@B}.", new { BookPlainTextGuid = bookPlainText.BookGuid });

                    inPackageTaskCount++;
                    Logs.Here().Information("Hash version was added to {0} book plain text(s).", inPackageTaskCount);

                    // создать поле плоского текста
                    await _cache.WriteHashedAsync<TextSentence>(taskPackageGuid, f, bookPlainText, taskPackageGuidLifeTime);
                    Logs.Here().Information("Plain text {@F} No. {0} was created in {@K}.", new { Filed = f }, inPackageTaskCount, new { Key = taskPackageGuid });
                }
            }

            if (inPackageTaskCount == 1) // !
            {
                // как правило это будет относиться к обоим книгам пары
                // надо решить, что делать если совпадает только одна книга из пары
                // если ничего не делать, то сейчас она одна запишется с новой версией
                // и с этого момента номера версий двух языков пары перестанут совпадать
                // надо что-то решить по этому поводу -
                // 1 можно удалить нечётную (без пары) книгу и сообщить, что ничего не получилось
                // 2 можно дописать парную книгу-пустышку с таким же номером хэш-версии
                // в принципе, вполне рабочая ситуация, что отредактирована только одна книга из пары и надо что-то делать с этим
            }

            if (inPackageTaskCount == 0)
            {
                // в конце, при возврате taskPackageGuid проверять счётчик
                // если ничего не насчитал, то возвратить null - нет задач для пакета
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
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list

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
                Logs.Here().Warning("This plain text already exists. {@B} / {@L}.", new { BookId = bookId }, new { LanguageId = languageId });
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

        // метод создаёт элемент List-хранилища хэшей плоских текстов и обновляет сам плоский текст, добавляя в него хэш и версию текста
        private async Task<TextSentence> WriteBookPlainTextHash(ConstantsSet constantsSet, TextSentence bookPlainText, List<TextSentence> bookPlainTextsHash, int versionHash, string bookPlainTextMD5Hash)
        {
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
            double keyBookPlainTextsHashesVersionsListLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.LifeTime; // 1000

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
                // _prepare.SomethingWentWrong(!soughtKey);
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


        // убрать в общую библиотеку

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


        #endregion



        public async Task<List<int>> CreateTestBookIdsListFromStorageKey(ConstantsSet constantsSet, string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test")
        {
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000

            List<int> uniqueBookIdsFromStorageKey = new();

            bool storageKeyBookPlainTextsIsExisted = await _cache.IsKeyExist(storageKeyBookPlainTexts);
            if (!storageKeyBookPlainTextsIsExisted)
            {
                SomethingWentWrong(storageKeyBookPlainTextsIsExisted);
                return null;
            }

            IDictionary<string, TextSentence> fieldsFromStorageKeyBookPlainTexts = await _cache.FetchHashedAllAsync<TextSentence>(storageKeyBookPlainTexts);

            foreach (KeyValuePair<string, TextSentence> s in fieldsFromStorageKeyBookPlainTexts)
            {
                (_, TextSentence v) = s;

                // пишем в уникальный список только базовые номера книг
                int b = v.BookId;// + v.LanguageId * chapterFieldsShiftFactor;

                //if (uniqueBookIdsFromStorageKeyCount == 0 || (uniqueBookIdsFromStorageKeyCount > 0 && !uniqueBookIdsFromStorageKey.Contains(b)))
                // и надо проверить, что в тестовом наборе у каждой английской книги есть пара - русская
                // самое простое - можно сначала писать уникальные номера обоих языков, а потом проверить парность и номера русских книг выкинуть
                // потом добавить, а пока считаем, что в тестовых книгах всегда пары

                if (!uniqueBookIdsFromStorageKey.Contains(b))
                {
                    uniqueBookIdsFromStorageKey.Add(b);
                    Logs.Here().Information("Unique BookId {0} from StorageKey was added to List.", b);
                }
            }
            return uniqueBookIdsFromStorageKey;
        }









        // проверить наличие ключа-хранилища, достать из него список (словарь) полей (и значений), перегнать поля в список
        // вызвать метод, передать ему ключ и список полей - метод вернёт ключ с полями, но он не нужен
        // ещё метод запишет в вечный лог поля с номерами книг
        // string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
        // эти номера хранятся в константах (массив?) и можно достать из лога нужные гуид-поля с текстами
        // нет, достаём номера книг с учётом языка (собственно, в точности, как поля в логе), сохраняем их в список и возвращаем из метода
        // сразу после метода можно использовать поля из лога - не совсем - ещё нет связи между этими полями и гуид-полями тестового хранилища
        // на выходе можно достать тексты по цифровым полям и сравнить хэши тестов из хранилища с теми, что хранятся в логе (но непонятно, зачем)
        // 

        public async Task<List<int>> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0)
        {
            string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test";

            string testKeyBookPlainTextsPrefix = constantsSet.BookPlainTextConstant.KeyPrefix.Value; // bookPlainTexts:bookSplitGuid
            string testKeyBookPlainTextsGuid = "5a272735-4be3-45a3-91fc-152f5654e451";
            string testKeyBookPlainTexts = $"{testKeyBookPlainTextsPrefix}:{testKeyBookPlainTextsGuid}"; // bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451

            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
            double keyBookPlainTextsHashesVersionsListLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.LifeTime; // 1000
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string testKeyBookPlainTextsHashesVersionsList = $"{keyBookPlainTextsHashesVersionsList}:{eventKeyTest}";

            // сюда можно передать ключ временного хранилища тестовых плоских текстов, если он будет как-то вычисляться, а не генерироваться контроллером
            List<int> uniqueBookIdsFromStorageKey = await CreateTestBookIdsListFromStorageKey(constantsSet); //, string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test")

            await CreateTestScenarioLists(constantsSet, uniqueBookIdsFromStorageKey);








            List<string> fieldsFromStorageKey = new();
            //List<int> uniqueBookIdsFromStorageKey = new();
            int uniqueBookIdsFromStorageKeyCount = uniqueBookIdsFromStorageKey.Count;
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
            Dictionary<string, int> bookIdsFromStorageKey = new();

            bool storageKeyBookPlainTextsIsExisted = await _cache.IsKeyExist(storageKeyBookPlainTexts);
            if (!storageKeyBookPlainTextsIsExisted)
            {
                SomethingWentWrong(storageKeyBookPlainTextsIsExisted);
                return null;
            }

            bool testKeyBookPlainTextsHashesVersionsListIsExisted = await _cache.IsKeyExist(testKeyBookPlainTextsHashesVersionsList);
            if (!testKeyBookPlainTextsHashesVersionsListIsExisted)
            {
                IDictionary<string, TextSentence> fieldsFromStorageKeyBookPlainTexts = await _cache.FetchHashedAllAsync<TextSentence>(storageKeyBookPlainTexts);
                int fieldsFromStorageKeyBookPlainTextsCount = fieldsFromStorageKeyBookPlainTexts.Count;

                foreach (var s in fieldsFromStorageKeyBookPlainTexts)
                {
                    (var f, var v) = s;
                    int b = v.BookId + v.LanguageId * chapterFieldsShiftFactor;
                    fieldsFromStorageKey.Add(f);
                    //if (uniqueBookIdsFromStorageKeyCount == 0 || (uniqueBookIdsFromStorageKeyCount > 0 && !uniqueBookIdsFromStorageKey.Contains(b)))
                    if (!uniqueBookIdsFromStorageKey.Contains(b))
                    {
                        uniqueBookIdsFromStorageKey.Add(b);
                    }


                    bookIdsFromStorageKey.Add(f, b);
                    Logs.Here().Information("{@F} from Storage Key and the same as in the log {@B} were added to Dictionary.", new { Filed = f }, new { BookId = b });
                }
            }
            // на выходе в словаре будут поля (ключи) и номера книг/языков (значения), каждый номер по числу версий
            // но это мало что даёт, нужны версии
            // всё же придётся доставать значения полей из вечного лога - с другой стороны, тогда не надо его стирать

            string taskPackageGuid = await CreateTaskPackageAndSaveLog(constantsSet, storageKeyBookPlainTexts, fieldsFromStorageKey);
            // если тестовые задачи уже есть в вечном логе, то тут будет нул
            // и где-то надо взять номера книг (как минимум)

            // чтобы долго не возиться, можно переписать отсюда обратно в ключ-хранилище уже готовые модифицированные тексты
            // только сначала проверить, что там везде то, что нужно
            // не надо ничего переписывать
            // надо определить нужное количество полей (количество пар книг) - получили в параметрах
            // удалить лишние поля из выходного ключа taskPackageGuid
            // и удалить все тестовые поля <int> из вечного лога

            // 1 определить, какой номер книги (пары) использовать и какую версию


            // Value Type is TextSentence

            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
                                                                   // пока время тестовых ключей задаётся отдельно (может, меньше, чем настоящие) - хотя реальной разницы нет
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime

            // проверить ключи плоского текста и тестового оповещения и, если нужно, удалить их
            // пока что удаляем при старте
            bool resultPlainText = await RemoveWorkKeyOnStart(testKeyBookPlainTexts);
            bool resultFromTest = await RemoveWorkKeyOnStart(eventKeyFrom);

            Logs.Here().Information("Test plain text keys creation is started");


            return default;
        }

        private List<T> OperationWithListFirstElement<T>(T theFirstElement)
        {
            List<T> fieldsFromStorageKey = new();
            fieldsFromStorageKey.Add(theFirstElement);
            return fieldsFromStorageKey;
        }

        private bool IsValueAlreadyInList<T>(List<T> testeeList, T testeeValue) where T : class
        {
            foreach (T v in testeeList)
            {
                if (v == testeeValue)
                {

                }
            }

            return false;
        }

        // можно сделать перегрузку с массивом на вход
        // true соответствует печали
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "")
        { // return true if something went wrong!
            const int resultCount = 5;
            bool[] results = new bool[resultCount] { result0, result1, result2, result3, result4 };

            for (int i = 0; i < resultCount; i++)
            {
                if (!results[i])
                {
                    Logs.Here().Error("Situation in {0} where something went unexpectedly wrong is appeared - result No. {1} is {2}", currentMethodName, results[i], i);
                    return true;
                }
            }
            return false;
        }


        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            // can use Task RemoveAsync(string[] keys, CommandFlags flags = CommandFlags.None);
            bool result = await _cache.IsKeyExist(key);
            if (result)
            {
                result = await _cache.DeleteKeyIfCancelled(key);
                Logs.Here().Information("{@K} was removed with result {0}.", new { Key = key }, result);
                return result;
            }
            Logs.Here().Information("{@K} does not exist.", new { Key = key });
            return !result;
        }

    }
}
