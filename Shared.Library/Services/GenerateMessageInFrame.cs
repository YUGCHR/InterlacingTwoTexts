using System;

namespace Shared.Library.Services
{
    public static class GenerateMessageInFrame
    {
        public static (string, string) CreateMeassageInFrame(char separatorUnit, string successTextMessage)
        {
            string inFrameTextMessage = $"{separatorUnit} {successTextMessage} {separatorUnit}";
            int inFrameTextMessageLength = inFrameTextMessage.Length;
            string frameSeparator1 = new(separatorUnit, inFrameTextMessageLength);
            return (frameSeparator1, inFrameTextMessage);
        }
    }
}
