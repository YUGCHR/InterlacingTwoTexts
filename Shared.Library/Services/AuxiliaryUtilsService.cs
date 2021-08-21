using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shared.Library.Models;

#region TestRawBookTextsStorageService description




#endregion

namespace Shared.Library.Services
{
    public interface IAuxiliaryUtilsService
    {
        public bool SomethingWentWrong(bool result0, bool result1 = true, bool result2 = true, bool result3 = true, bool result4 = true, [CallerMemberName] string currentMethodName = "");
        public Task<bool> IsTestInProgress(ConstantsSet constantsSet);
        public Task<bool> RemoveWorkKeyOnStart(string key);
    }

    public class AuxiliaryUtilsService : IAuxiliaryUtilsService
    {
        private readonly ICacheManagerService _cache;

        public AuxiliaryUtilsService(ICacheManagerService cache)
        {
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<AuxiliaryUtilsService>();




        // можно сделать перегрузку с массивом на вход
        // false !!! соответствует печали
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


        public async Task<bool> IsTestInProgress(ConstantsSet constantsSet)
        {
            // здесь проверить тестовый ключ и, если выполняется тест, ждать
            // можно ждать стандартные 5 сек (или свое время) и, если не освободилось, возвращать пользователю отлуп
            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            int delayTimeForTest1 = constantsSet.IntegerConstant.IntegrationTestConstant.DelayTimeForTest1.Value; // 1000
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value; // 5000
            int totalTimeOfTestEndWaiting = (int)(timerIntervalInMilliseconds * 2.001) / delayTimeForTest1;
            int currentTimeToWaitEndOfTest = 0;

            bool isTestInProgress = true;
            // крутимся в цикле, пока существует ключ запуска теста
            while (isTestInProgress)
            {
                isTestInProgress = await _cache.IsKeyExist(eventKeyTest);
                Logs.Here().Information("Test {@K} is existed - {0}", new { Key = eventKeyTest }, isTestInProgress);

                if (!isTestInProgress)
                {
                    // если ключа теста нет, возвращаем, что теста нет - без ожидания
                    Logs.Here().Information("Test {@K} is not found - {0}", new { Key = eventKeyTest }, !isTestInProgress);
                    return false;
                }

                await Task.Delay(delayTimeForTest1);
                currentTimeToWaitEndOfTest++;
                Logs.Here().Information("Test {@K} is existed - {0}, it is waited {1} msec for {2} times", new { Key = eventKeyTest }, isTestInProgress, delayTimeForTest1, currentTimeToWaitEndOfTest);

                if (currentTimeToWaitEndOfTest > totalTimeOfTestEndWaiting)
                {
                    // время ожидания вышло, тест почему-то не закончился - всё плохо, больше не ждём
                    Logs.Here().Information("The waiting time {0} was expired {1}, the test {@K} for some reason is {2} yet", currentTimeToWaitEndOfTest, totalTimeOfTestEndWaiting, new { Key = eventKeyTest }, isTestInProgress);

                    return true;
                }
            }
            // вообще всё пропало
            return true;
        }

        public async Task<bool> RemoveWorkKeyOnStart(string key)
        {
            // can use Task RemoveAsync(string[] keys, CommandFlags flags = CommandFlags.None);
            bool resultExist = await _cache.IsKeyExist(key);
            if (resultExist)
            {
                bool resultDelete = await _cache.DeleteKeyIfCancelled(key);
                Logs.Here().Information("{@K} was removed with result {0}.", new { Key = key }, resultDelete);
                return resultDelete;
            }
            Logs.Here().Information("Is {@K} exist - {0}.", new { Key = key }, resultExist);
            return !resultExist;
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




















    }
}