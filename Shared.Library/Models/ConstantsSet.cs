﻿using System;
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

            BookTextSplitLater = new();
            BookTextSplitLater.Guid = new KeyType();
            BookTextSplitLater.PrefixGuid = new KeyType();

            BookPlainTextLater = new();
            BookPlainTextLater.KeyPrefixGuid = new KeyType();

            BackgroundDispatcherLater = new();
            BackgroundDispatcherLater.Guid = new KeyType();
            BackgroundDispatcherLater.PrefixGuid = new KeyType();
        }

        // ConstantsList
        public ConstantType RecordActualityLevel { get; set; }
        public ConstantType TaskEmulatorDelayTimeInMilliseconds { get; set; }
        public ConstantType RandomRangeExtended { get; set; }
        public ConstantType BalanceOfTasksAndProcesses { get; set; }
        public ConstantType MaxProcessesCountOnServer { get; set; }
        public ConstantType MinBackProcessesServersCount { get; set; }
        public ConstantType ChapterFieldsShiftFactor { get; set; } // shift chapter numbers of the second language (for example + 1 000 000)

        // KeysList
        public KeyType EventKeyFrom { get; init; } // subscribeOnFrom - key to fetch task package from controller/emulator
        public KeyType EventKeyBackReadiness { get; init; } // key-event-back-processes-servers-readiness-list - key for back-servers registration
        public KeyType EventKeyFrontGivesTask { get; init; } // key-event-front-server-gives-task-package - key for tasks cafe
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

        // KeysList - BookTextSplit
        // добавить(под)классы BookTextSplit, BookPlainText, BookTables, TextSentences 
        public KeyType BookTextSplitPrefix { get; set; } // BookTextSplit front server Prefix
        public KeyType BookPlainTextKeyPrefix { get; set; } // bookPlainTexts:bookSplitGuid: - key prefix for book text pass to back server
        public KeyType BookPlainTextFieldPrefix { get; set; } // bookText:bookGuid: - field prefix for book text pass to back server        
        public KeyType BookTablesKeyPrefix { get; set; } // bookTables:bookId: - this prefix + bookId is the key of all version of this bookId
        public KeyType TextSentencesKeyPrefixId { get; set; } // textSentences:bookId: - chapters key prefix part 1 (part1 + bookId + part2 + upld-ver)
        public KeyType TextSentencesKeyPrefixVer { get; set; } // uploadVersion:

        public BackgroundDispatcherPrefix BackgroundDispatcher { get; set; }
        public class BackgroundDispatcherPrefix
        {
            public KeyType Prefix { get; set; } // BackgroundDispatcher Prefix
            public KeyType TempTest { get; set; } // Test            
        }

        // ---------- LaterAssigned ----------

        public KeyEvent EventCmd { get; init; } 
        public KeyType ConstantsVersionBase { get; set; }
        public KeyType ConstantsVersionBaseField { get; set; }
        public ConstantType ConstantsVersionNumber { get; set; }
        public KeyType BackServerGuid { get; set; }
        public KeyType BackServerPrefixGuid { get; set; }
        public KeyType ProcessAddPrefixGuid { get; set; }
        public KeyType ProcessCancelPrefixGuid { get; set; }
        public KeyType ProcessCountPrefixGuid { get; set; }

        // LaterAssigned

        public BookTextSplitGuid BookTextSplitLater { get; set; }

        public class BookTextSplitGuid
        {
            public KeyType Guid { get; set; } // BookTextSplit Guid
            public KeyType PrefixGuid { get; set; } // BookTextSplit Prefix:Guid        
        }

        public BookPlainTextGuid BookPlainTextLater { get; set; }

        public class BookPlainTextGuid
        {            
            public KeyType KeyPrefixGuid { get; set; } // bookPlainTexts:bookSplitGuid: + BookTextSplit Guid       
        }

        public BackgroundDispatcherGuid BackgroundDispatcherLater { get; set; }

        public class BackgroundDispatcherGuid
        {
            public KeyType Guid { get; set; } // BackgroundDispatcher Guid
            public KeyType PrefixGuid { get; set; } // BackgroundDispatcher Prefix:Guid        
        }

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
