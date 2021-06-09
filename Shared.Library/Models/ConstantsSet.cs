using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts;

namespace Shared.Library.Models
{
    // former EventKeyNames
    public class ConstantsSet
    {
        public ConstantsSet()
        {
            EventCmd = KeyEvent.HashSet;
            ConstantsVersionBase = new KeyType();
            ConstantsVersionBaseField = new KeyType();
            ConstantsVersionNumber = new ConstantType
            {
                Value = 0
            };
            BackServerGuid = new KeyType();
            BackServerPrefixGuid = new KeyType();
            ProcessAddPrefixGuid = new KeyType();
            ProcessCancelPrefixGuid = new KeyType();
            ProcessCountPrefixGuid = new KeyType();
        }

        // ConstantsList
        public ConstantType RecordActualityLevel { get; set; }
        public ConstantType TaskEmulatorDelayTimeInMilliseconds { get; set; }
        public ConstantType RandomRangeExtended { get; set; }
        public ConstantType BalanceOfTasksAndProcesses { get; set; }
        public ConstantType MaxProcessesCountOnServer { get; set; }
        public ConstantType MinBackProcessesServersCount { get; set; }

        // KeysList
        public KeyType EventKeyFrom { get; init; }
        public KeyType EventKeyBackReadiness { get; init; }
        public KeyType EventKeyFrontGivesTask { get; init; }
        public KeyType EventKeyUpdateConstants { get; init; }
        public KeyType EventKeyBacksTasksProceed { get; init; }
        public KeyType PrefixRequest { get; init; }
        public KeyType PrefixPackage { get; init; }
        public KeyType PrefixPackageControl { get; init; }
        public KeyType PrefixPackageCompleted { get; init; }
        public KeyType PrefixTask { get; init; }
        public KeyType PrefixDataServer { get; init; }
        public KeyType PrefixBackServer { get; init; }
        public KeyType PrefixProcessAdd { get; init; }
        public KeyType PrefixProcessCancel { get; init; }
        public KeyType PrefixProcessCount { get; init; }
        public KeyType FinalPropertyToSet { get; init; }
        public KeyType EventFieldFrom { get; init; }
        public KeyType EventFieldBack { get; init; }
        public KeyType EventFieldFront { get; init; }

        // LaterAssigned

        public KeyEvent EventCmd { get; init; } 
        public KeyType ConstantsVersionBase { get; set; }
        public KeyType ConstantsVersionBaseField { get; set; }
        public ConstantType ConstantsVersionNumber { get; set; }
        public KeyType BackServerGuid { get; set; }
        public KeyType BackServerPrefixGuid { get; set; }
        public KeyType ProcessAddPrefixGuid { get; set; }
        public KeyType ProcessCancelPrefixGuid { get; set; }
        public KeyType ProcessCountPrefixGuid { get; set; }
    }

    public class ConstantType
    {
        public string Description { get; set; }
        public int Value { get; set; }
        public double LifeTime { get; set; }
    }

    public class KeyType
    {
        public string Description { get; set; }
        public string Value { get; set; }
        public double LifeTime { get; set; }
    }
}
