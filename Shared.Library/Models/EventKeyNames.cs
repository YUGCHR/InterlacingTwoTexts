using CachingFramework.Redis.Contracts;
 
namespace Shared.Library.Models
{
    // common constants model for 3 solutions
    public class EventKeyNames
    {
        // версия обновления констант - присваивается сервером констант
        public string ConstantsVersionBase { get; set; }
        public int ConstantsVersionNumber { get; set; }
        // Backserver guid - will be set in BackgroundTasksQueue
        public string BackServerGuid { get; set; }
        // backserver:(this Backserver guid) - will be set in BackgroundTasksQueue
        public string BackServerPrefixGuid { get; set; }
        // process:add:(this server guid) - will be set in BackgroundTasksQueue
        public string ProcessAddPrefixGuid { get; set; }
        // process:cancel:(this server guid) - will be set in BackgroundTasksQueue
        public string ProcessCancelPrefixGuid { get; set; }
        // process:count:(this server guid) - will be set in BackgroundTasksQueue
        public string ProcessCountPrefixGuid { get; set; }


        // время задержки в секундах для эмулятора счета задачи
        public int TaskEmulatorDelayTimeInMilliseconds { get; set; }

        // верхний предел для генерации случайного числа - расширенный (например, миллион)
        public int RandomRangeExtended { get; set; }

        // соотношение количества задач и процессов для их выполнения на back-processes-servers (количества задач разделить на это число и сделать столько процессов)
        public int BalanceOfTasksAndProcesses { get; set; }
        // максимальное количество процессов на back-processes-servers (минимальное - 1)
        public int MaxProcessesCountOnServer { get; set; }

        // for FrontEmulator only
        public int MinBackProcessesServersCount { get; set; }

        // срок хранения ключа Common
        public double EventKeyCommonKeyTimeDays { get; set; }
        // срок хранения ключа eventKeyFrom
        public double EventKeyFromTimeDays { get; set; }
        // срок хранения ключа 
        public double EventKeyBackReadinessTimeDays { get; set; }
        // срок хранения ключа 
        public double EventKeyFrontGivesTaskTimeDays { get; set; }
        // срок хранения ключа 
        public double EventKeyBackServerMainTimeDays { get; set; }
        // срок хранения ключа 
        public double EventKeyBackServerAuxiliaryTimeDays { get; set; }
        // for Controller only
        public double PercentsKeysExistingTimeInMinutes { get; set; }

        // группа констант, которые нельзя менять после инициализации
        // "subscribeOnFrom" - ключ для подписки на команду запуска эмулятора сервера
        public string EventKeyFrom { get; init; }
        // "count" - поле для подписки на команду запуска эмулятора сервера
        public string EventFieldFrom { get; init; }
        // операция для подписки
        public KeyEvent EventCmd { get; init; }

        // 
        public int RandomSeedFromGuid { get; set; }

        // ключ регистрации серверов
        public string EventKeyBackReadiness { get; init; }
        // универсальное поле-заглушка - чтобы везде одинаковое
        public string EventFieldBack { get; init; }
        // кафе выдачи задач
        public string EventKeyFrontGivesTask { get; init; }
        // UNUSED - constants updating key
        public string EventKeyUpdateConstants { get; init; }
        // Prefix - request:guid
        public string PrefixRequest { get; init; }
        // Prefix - package:guid
        public string PrefixPackage { get; init; }
        // Prefix - control:package:guid
        public string PrefixPackageControl { get; init; }
        // Prefix - completed:package:guid
        public string PrefixPackageCompleted { get; init; }
        // Prefix - task:guid
        public string PrefixTask { get; init; }
        // Prefix - backserver:guid
        public string PrefixBackServer { get; init; }
        // Prefix - process:add
        public string PrefixProcessAdd { get; init; }
        // Prefix - process:cancel
        public string PrefixProcessCancel { get; init; }
        // Prefix - process:count
        public string PrefixProcessCount { get; init; }
        // UNUSED - ?
        public string EventFieldFront { get; init; }
        // ключ выполняемых/выполненных задач
        public string EventKeyBacksTasksProceed { get; init; }
    }
}
