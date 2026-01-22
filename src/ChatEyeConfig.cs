using System.Collections.Generic;

namespace ChatEye
{
    public class KeywordEntry
    {
        public string Trigger = "";
        public string ReplyMessage = ""; 
        
        public string Prefix = "Info:"; 
        
        public string PrefixColor = "#F5E945"; 
        public int CooldownSeconds = 300;
    }

    public class ChatEyeConfig
    {
        public bool CreateServerLogs = true;
        public bool SendLogsToDiscord = false;
        public string DiscordWebhookUrl = "";

        public List<KeywordEntry> GeneralKeywords = new List<KeywordEntry>();
        public List<KeywordEntry> ObsceneKeywords = new List<KeywordEntry>();

        public string GeneralLogName = "chateye-general.log";
        public string ObsceneLogName = "chateye-obscene.log";
    }
}
