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
using Serilog.Events;
using Shared.Library.Models;
using Shared.Library.Services;

#region CollectTasksInPackage description




#endregion

namespace BackgroundDispatcher.Services
{
    public interface ICollectTasksInPackageService
    {
        public Task<string> CreateTaskPackageAndSaveLog(ConstantsSet constantsSet, int currentChainSerialNum, string sourceKeyWithPlainTexts, List<string> taskPackageFileds, bool isProceededWorkTask = false);
    }

    public class CollectTasksInPackageService : ICollectTasksInPackageService
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly IEternalLogSupportService _eternal;
        private readonly ICacheManagerService _cache;

        public CollectTasksInPackageService(
            IAuxiliaryUtilsService aux,
            IEternalLogSupportService eternal,
            ICacheManagerService cache)
        {
            _aux = aux;
            _eternal = eternal;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<CollectTasksInPackageService>();

        // вне теста этот метод используется для для создания ключа готового пакета задач -
        // с последующей генерацией (другим методом) ключа кафе для оповещения о задачах бэк-сервера
        // сохраняются названия гуид-полей книг, созданные контроллером, но они перезаписываются в новый ключ, уникальный для собранного пакета
        // одновременно, при перезаписи содержимого книг, оно анализируется (вычисляется хэш текста) и проверяется на уникальность
        // если такая книга уже есть, это гуид-поле удаляется
        // здесь этот метод используется для записи хэшей в вечный лог -
        // при этом вычисляются номера версий загружаемых книг, что и нужно вызывающему методу
        public async Task<string> CreateTaskPackageAndSaveLog(ConstantsSet constantsSet, int currentChainSerialNum, string sourceKeyWithPlainTexts, List<string> taskPackageFileds, bool isProceededWorkTask = false)
        {
            // план действий метода -
            // генерируем новый гуид - это будет ключ пакета задач
            // достаём по одному тексты и складываем в новый ключ
            // гуид пакета отдаём в следующий метод

            if (sourceKeyWithPlainTexts == null)
            {
                _aux.SomethingWentWrong(false);
                return "";
            }

            string taskPackage = constantsSet.Prefix.BackgroundDispatcherPrefix.TaskPackage.Value; // taskPackage
            double taskPackageGuidLifeTime = constantsSet.Prefix.BackgroundDispatcherPrefix.TaskPackage.LifeTime; // 0.01
            string currentPackageGuid = Guid.NewGuid().ToString();
            string taskPackageGuid = $"{taskPackage}:{currentPackageGuid}"; // taskPackage:guid

            //List<bool> resultPlainText = new();
            int inPackageTaskCount = 0;

            foreach (string f in taskPackageFileds)
            {
                // прочитать первое поле хранилища
                TextSentence bookPlainText = await _cache.FetchHashedAsync<TextSentence>(sourceKeyWithPlainTexts, f);
                if (isProceededWorkTask)
                {
                    Logs.Here().Information("Test plain text was read from key-storage");
                }

                // тут вроде бы можно удалить исходный ключ, который сейчас имя поля f
                bool fWasDeleted = await _aux.RemoveWorkKeyOnStart(f);
                if (fWasDeleted && isProceededWorkTask)
                {
                    Logs.Here().Information("{@B} was deleted successfully.", new { Key = f });
                }

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
                bookPlainText = await _eternal.AddVersionViaHashToPlainText(constantsSet, bookPlainText, isProceededWorkTask);
                // может вернуться null, надо придумать, что с ним делать - это означает, что такой текст есть и работать с ним не надо
                // вместо null вернётся пустой объект TextSentence, надо придумать, как его опознать
                // не проверяется второй текст и, очевидно, всё следующие в пакете
                // возвращать null не надо, просто не будем записывать - и создавать поле задачи в пакете задач

                if (bookPlainText.BookId != 0)
                {
                    inPackageTaskCount++;
                    // создать поле плоского текста
                    await _cache.WriteHashedAsync<TextSentence>(taskPackageGuid, f, bookPlainText, taskPackageGuidLifeTime);
                    if (isProceededWorkTask)
                    {
                        Logs.Here().Information("Hash version was added to {@B}.", new { BookPlainTextGuid = bookPlainText.BookGuid });
                        Logs.Here().Information("Hash version was added to {0} book plain text(s).", inPackageTaskCount);
                        Logs.Here().Information("Plain text {@F} No. {0} was created in {@K}.", new { Filed = f }, inPackageTaskCount, new { Key = taskPackageGuid });
                    }
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
                if (isProceededWorkTask)
                {
                    Logs.Here().Information("Hash version was added in 0 cases.");
                }
                return "";
            }

            return taskPackageGuid;
        }

    }
}
