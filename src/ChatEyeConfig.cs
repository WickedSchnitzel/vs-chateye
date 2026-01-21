using System.Collections.Generic;

namespace ChatEye
{
    public class ChatEyeConfig
    {
        // 1. Einstellungen (Schalter & URL)
        public bool CreateServerLogs = true;
        public bool SendLogsToDiscord = false;
        public string DiscordWebhookUrl = "";

        // 2. Listen für Keywords
        public List<string> GeneralKeywords = new List<string>();
        public List<string> ObsceneKeywords = new List<string>();

        // 3. Dateinamen
        public string GeneralLogName = "chateye-general.log";
        public string ObsceneLogName = "chateye-obscene.log";
    }
}