using System;
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

namespace BackgroundDispatcher.Services
{
    public interface ITestTasksPrepareAndStoreService
    {
        public Task<string> CreateTaskPackageAndSaveLog(ConstantsSet constantsSet, string sourceKeyWithPlainTexts, List<string> taskPackageFileds);
        public Task<bool> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0);
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

        // метод создаёт тестовые плоские тексты для тестов
        // берет нужное количество книг/версий для сценария из хранилища
        // и из них делает ключи, неотличимые от приходящих из веб-интерфейса
        // если сценарий предусматривает, то по окончанию теста новые ключи с хэшами должны быть удалены из хранилища хэшей-версий

        // во время теста (при старте) -
        // проверить наличие ключа-хранилища тестовых задач
        // если ключа нет, то и тест надо прекратить (может, в зависимости от сценария)

        // проверить наличие ключа созданных описаний тестовых задач
        // если ключа нет, надо его создать -
        // считать всё из ключа-хранилища
        // прочитать у каждого поля номер книги и прочее
        // записать в ключ описаний листом или по полям, поля как в ключе хранения лога - номера книг и со смещением признак языка

        // CreateTaskPackageAndSaveLog
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


















        public async Task<bool> CreateBookPlainTextsForTests(ConstantsSet constantsSet, CancellationToken stoppingToken, int testPairsCount = 1, int delayAfter = 0)
        {
            string storageKeyBookPlainTexts = "bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test";
            // hget bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451:test bookText:bookGuid:9902e124-5dc1-4ad7-9dce-428070a97d59

            //string field_ = "bookText:bookGuid:9902e124-5dc1-4ad7-9dce-428070a97d59"; // 71 - 1 - 17
            //string field_ = "bookText:bookGuid:ff67769e-ef73-41e4-a317-a313d641b252"; // 71 - 0 - 15
            //string field_ = "bookText:bookGuid:058799d2-04bb-4025-baaa-561612cf9ab3"; // 73 - 1 - 28
            //string field_ = "bookText:bookGuid:45d9b924-d9d3-47cb-8154-23c659a538be";
            //string field_ = "bookText:bookGuid:0e58ba37-8d5b-456e-820c-950b7b99b1eb";
            //string field_ = "bookText:bookGuid:3d21e753-94c2-4dcc-a8e2-d9003c60ab19";
            //string field_ = "bookText:bookGuid:7604fc0b-511f-467d-8fe4-83cb60cce49e";
            //string field_ = "bookText:bookGuid:b69189d3-4da5-437e-a6fe-71a18703c3f5";
            //string field_ = "bookText:bookGuid:c407d7d0-20ab-4c98-9cfa-102196a333bf";
            //string field_ = "bookText:bookGuid:5970f94c-99bd-4b01-b628-03c2acb90074";
            //string field_ = "bookText:bookGuid:84d94f49-9c6d-44aa-b2f3-ac40b610c823";
            //string field_ = "bookText:bookGuid:d74ceb93-7519-4b22-b165-bac0ef6c3536";
            //string field_ = "bookText:bookGuid:a0e5e2fb-e1a2-4d08-99d0-95d4a2096ba0";
            //string field_ = "bookText:bookGuid:0eaa8879-6bf0-4961-8b26-c6b6ecfcd0ee";
            //string field_ = "bookText:bookGuid:9eb0544b-2f7e-4e89-a847-8cd619c08e28";
            //string field_ = "bookText:bookGuid:2f24a7af-caa8-49db-907a-32efbedce26e";
            //string field_ = "bookText:bookGuid:b1f9b81e-ffde-4244-bec8-8bdfca9fa647";
            //string field_ = "bookText:bookGuid:7b9c873d-83c1-4e94-bc9f-28a3a96aab93";
            //string field_ = "bookText:bookGuid:e3589686-eef2-49e6-94a2-e799af2fab03";
            //string field_ = "bookText:bookGuid:6a8c2d5a-552e-4833-9385-31f83f906416";
            //string field_ = "bookText:bookGuid:dade4abd-9bf4-4fc7-8db4-6f012e8b21f1";
            //string field_ = "bookText:bookGuid:71cb7a2e-a71d-43ff-968f-bcfe1fe1d0ec";
            //string field_ = "bookText:bookGuid:e031e392-9065-4ba0-84fa-1c48b3143820";
            //string field_ = "bookText:bookGuid:34c7ac9d-3934-4bd7-a554-08bc6d72587c";
            //string field_ = "bookText:bookGuid:6b79d96e-5bb7-4e86-a2eb-0311ae69bb64";
            //string field_ = "bookText:bookGuid:c4a0d1bd-a525-4095-9f23-b0a24d166422";
            //string field_ = "bookText:bookGuid:9488cd35-3e11-4675-a4c5-918fc25b25a8";
            //string field_ = "bookText:bookGuid:74e9173b-b9c5-46fd-8d6c-085923d4a523";
            //string field_ = "bookText:bookGuid:a797dc1c-42eb-4b9d-920b-63b789349f03";
            //string field_ = "bookText:bookGuid:fd18b5fd-6d32-4e31-9630-c0446ba09831";


            string testKeyBookPlainTextsPrefix = constantsSet.BookPlainTextConstant.KeyPrefix.Value; // bookPlainTexts:bookSplitGuid
            string testKeyBookPlainTextsGuid = "5a272735-4be3-45a3-91fc-152f5654e451";
            string testKeyBookPlainTexts = $"{testKeyBookPlainTextsPrefix}:{testKeyBookPlainTextsGuid}"; // bookPlainTexts:bookSplitGuid:5a272735-4be3-45a3-91fc-152f5654e451

            //"bookPlainTexts:bookSplitGuid:f0c17236-3d50-4bce-9843-15fc9ee79bbd";                                                                                                         
            //string[] fields = new string[4] { field_79_ENG, field_79_RUS, field_78_ENG, field_78_RUS };

            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.Value; // key-book-plain-texts-hashes-versions-list
            double keyBookPlainTextsHashesVersionsListLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.KeyBookPlainTextsHashesVersionsList.LifeTime; // 1000
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string testKeyBookPlainTextsHashesVersionsList = $"{keyBookPlainTextsHashesVersionsList}:{eventKeyTest}";

            bool storageKeyBookPlainTextsIsExisted = await _cache.IsKeyExist(storageKeyBookPlainTexts);
            if (!storageKeyBookPlainTextsIsExisted)
            {
                SomethingWentWrong(storageKeyBookPlainTextsIsExisted);
                return false;
            }

            bool testKeyBookPlainTextsHashesVersionsListIsExisted = await _cache.IsKeyExist(testKeyBookPlainTextsHashesVersionsList);
            if (!testKeyBookPlainTextsHashesVersionsListIsExisted)
            {
                IDictionary<string, TextSentence> fieldsFromStorageKeyBookPlainTexts = await _cache.FetchHashedAllAsync<TextSentence>(storageKeyBookPlainTexts);
                //IDictionary<string, TextSentence> fieldsFromHashesVersionsList = await _cache.FetchHashedAllAsync<TextSentence>(testKeyBookPlainTextsHashesVersionsList);
                int fieldsFromStorageKeyBookPlainTextsCount = fieldsFromStorageKeyBookPlainTexts.Count;
                //int fieldsFromHashesVersionsListCount = fieldsFromHashesVersionsList.Count;
                string theFirstStringElement = "00000";
                int theFirstIntElement = 1000;
                List<int> bufferGuidFieldsList = OperationWithListFirstElement<int>(theFirstIntElement);
                //HashesVersionsList

                foreach (var s in fieldsFromStorageKeyBookPlainTexts)
                {
                    (var f, var v) = s;
                    Logs.Here().Information("Dictionary element in Storage Key Book Plain Texts is {@F} {@V}.", new { Filed = f }, new { Value = v });

                    int bookId = v.BookId;
                    int languageId = v.LanguageId;
                    int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000
                    int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;

                    // List<T>.Contains(T) Method
                    // если список полей с номерами книг уже содержит такую книгу -
                    // достаём из этого поля список гуид полей текстов,
                    // добавляем в список новое гуид поле и кладём обратно
                    bool result = bufferGuidFieldsList.Contains(fieldBookIdWithLanguageId);
                    Logs.Here().Information("Fields (bookId) List contains {0} - is {1}.", fieldBookIdWithLanguageId, result);

                    if (result)
                    {
                        List<string> tempList = await _cache.FetchHashedAsync<int, List<string>>(testKeyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);
                        tempList.Add(f);
                        await _cache.WriteHashedAsync<int, List<string>>(testKeyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, tempList, keyBookPlainTextsHashesVersionsListLifeTime);
                        Logs.Here().Information("Fields (bookId) List contains {0} - is {1}.", fieldBookIdWithLanguageId, result);
                    }
                    // если не содержит - создаём новый список (с первым полем пустышкой), далее так же
                    // добавляем в список новое гуид поле и кладём обратно
                    // и добавляем в буферный список новое значение поля - номера книги
                    else
                    {
                        List<string> bufferFieldsList = OperationWithListFirstElement<string>(theFirstStringElement);
                        bufferFieldsList.Add(f);
                        await _cache.WriteHashedAsync<int, List<string>>(testKeyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bufferFieldsList, keyBookPlainTextsHashesVersionsListLifeTime);
                        //Logs.Here().Information("Test plain text was write in {@K} / {@F}", new { Key = testKeyBookPlainTexts }, new { Field = fields[i] });
                        bufferGuidFieldsList.Add(fieldBookIdWithLanguageId);
                    }

                    // 



                }

            }



            // Value Type is TextSentence

            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
                                                                   // пока время тестовых ключей задаётся отдельно (может, меньше, чем настоящие) - хотя реальной разницы нет
            double eventKeyFromTestLifeTime = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.LifeTime; // subscribeOnFrom:test lifeTime

            // проверить ключи плоского текста и тестового оповещения и, если нужно, удалить их
            // пока что удаляем при старте
            bool resultPlainText = await RemoveWorkKeyOnStart(testKeyBookPlainTexts);
            bool resultFromTest = await RemoveWorkKeyOnStart(eventKeyFrom);

            Logs.Here().Information("Test plain text keys creation is started");

            if (resultPlainText && resultFromTest)
            {
                // 1 считать ключ-хранилище тестовых плоских текстов
                // 2 создать лист модели из ключа, поля и значения плоского текста и к нему сигнального
                // (значение теста в лист не писать, доставать из кэша в момент записи нового ключа)

                // выделить for в отдельный метод и уменьшить слоистость?


                //for (int i = 0; i < testPairsCount * 2; i++)
                //{
                //    // прочитать первое поле хранилища
                //    TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(storageKeyBookPlainTexts, fields[i]);
                //    Logs.Here().Information("Test plain text was read from key-storage");

                //    // создать тестовый ключ плоского текста
                //    resultPlainText = await WriteHashedAsyncWithDelayAfter<TextSentence>(testKeyBookPlainTexts, fields[i], bookPlainText, eventKeyFromTestLifeTime, stoppingToken, delayAfter);
                //    Logs.Here().Information("Test plain text was write in {@K} / {@F}", new { Key = testKeyBookPlainTexts }, new { Field = fields[i] });

                //    // создать тестовый ключ оповещения 
                //    resultFromTest = await WriteHashedAsyncWithDelayAfter<string>(eventKeyFrom, fields[i], testKeyBookPlainTexts, eventKeyFromTestLifeTime, stoppingToken, delayAfter);
                //    Logs.Here().Information("Test subscribeOnFrom was write in {@K} / {@F} / {@V}", new { Key = eventKeyFrom }, new { Field = fields[i] }, new { Value = testKeyBookPlainTexts });

                //    if (SomethingWentWrong(resultPlainText, resultFromTest))
                //    {
                //        return false;
                //    }
                //}
                //Logs.Here().Information("Test pair(s) was created in quantity {0}.", testPairsCount);
                return true;
            }
            return !SomethingWentWrong(resultPlainText, resultFromTest);
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
