using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;

namespace ChatEye
{
    public class ChatEye : ModSystem
    {
        private ICoreServerAPI sapi;
        private ChatEyeConfig config;
        
        private string chatEyeLogDir;
        private string generalLogPath;
        private string obsceneLogPath;

        private static readonly HttpClient client = new HttpClient();
        private Dictionary<string, long> cooldownTracker = new Dictionary<string, long>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            try {
                config = sapi.LoadModConfig<ChatEyeConfig>("ChatEyeConfig.json");
            } catch { }

            if (config == null)
            {
                config = new ChatEyeConfig();
                
                config.GeneralKeywords.Add(new KeywordEntry 
                { 
                    Trigger = "placekeywordhere", 
                    ReplyMessage = "This is a test message including a <a href=\"https://yourlink\">Link</a>!",
                    Prefix = "Info:",
                    PrefixColor = "#F5E945",
                    CooldownSeconds = 300
                });
                
                sapi.StoreModConfig(config, "ChatEyeConfig.json");
            }

            string baseLogDir = api.GetOrCreateDataPath("Logs");
            chatEyeLogDir = Path.Combine(baseLogDir, "ChatEyeLogs");
            generalLogPath = Path.Combine(chatEyeLogDir, config.GeneralLogName);
            obsceneLogPath = Path.Combine(chatEyeLogDir, config.ObsceneLogName);

            if (config.CreateServerLogs && !Directory.Exists(chatEyeLogDir))
            {
                Directory.CreateDirectory(chatEyeLogDir);
            }

            sapi.Event.PlayerChat += OnPlayerChat;
        }

        private void OnPlayerChat(IServerPlayer player, int channelId, ref string message, ref string data, BoolRef consumed)
        {
            if (string.IsNullOrEmpty(message) || message.StartsWith("/")) return;

            // HTML Tags entfernen
            string cleanMessage = message;
            if (message.Contains("</font>"))
            {
                int lastTag = message.LastIndexOf(">");
                if (lastTag != -1 && lastTag < message.Length - 1)
                {
                    cleanMessage = message.Substring(lastTag + 1).Trim();
                }
            }

            // --- NEUE LOGIK ---
            // 1. Normale Kleinschreibung (für exakte Suche mit Leerzeichen)
            string lowerMsg = cleanMessage.ToLowerInvariant();
            
            // 2. Radikale Bereinigung: Nur Buchstaben und Zahlen behalten
            // Aus "dumm erpen n.e-r" wird "dummerpenner"
            string normalizedMsg = GetNormalizedString(lowerMsg);

            // 1. Obscene Check
            if (config.ObsceneKeywords != null)
            {
                foreach (var entry in config.ObsceneKeywords)
                {
                    if (CheckMatch(entry.Trigger, lowerMsg, normalizedMsg))
                    {
                        ProcessMatch("OBSCENE", obsceneLogPath, player, cleanMessage, entry.Trigger);
                        AttemptSendReply(player, entry);
                        return; 
                    }
                }
            }

            // 2. General Check
            if (config.GeneralKeywords != null)
            {
                foreach (var entry in config.GeneralKeywords)
                {
                    if (CheckMatch(entry.Trigger, lowerMsg, normalizedMsg))
                    {
                        ProcessMatch("GENERAL", generalLogPath, player, cleanMessage, entry.Trigger);
                        AttemptSendReply(player, entry); 
                        return; 
                    }
                }
            }
        }

        // Neue Hilfsfunktion: Entfernt alles, was kein Buchstabe oder Zahl ist
        private string GetNormalizedString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            StringBuilder sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                // Behält nur Buchstaben und Ziffern -> löscht Leerzeichen, Punkte, Bindestriche etc.
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private bool CheckMatch(string keyword, string lowerMsg, string normalizedMsg)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return false;
            
            string lowerWord = keyword.ToLowerInvariant();
            
            // Auch das Keyword müssen wir normalisieren, falls der Admin "how to" eingetragen hat -> "howto"
            string normalizedWord = GetNormalizedString(lowerWord);
            
            // Prüfung 1: Ist das Wort normal im Text? (Originalgetreu)
            // Prüfung 2: Ist das normalisierte Wort im normalisierten Text? (Catch-All)
            return lowerMsg.Contains(lowerWord) || normalizedMsg.Contains(normalizedWord);
        }

        private void AttemptSendReply(IServerPlayer player, KeywordEntry entry)
        {
            if (string.IsNullOrEmpty(entry.ReplyMessage)) return;

            long now = DateTimeOffset.Now.ToUnixTimeSeconds();
            string key = entry.Trigger.ToLowerInvariant();

            if (cooldownTracker.TryGetValue(key, out long lastTriggerTime))
            {
                if (now - lastTriggerTime < entry.CooldownSeconds) return; 
            }

            cooldownTracker[key] = now;
            sapi.Event.EnqueueMainThreadTask(() => SendReplyInternal(player, entry), "chateye_reply");
        }

        private void SendReplyInternal(IServerPlayer player, KeywordEntry entry)
        {
            if (player == null) return;

            string finalMessage = entry.ReplyMessage;

            if (!string.IsNullOrEmpty(entry.Prefix))
            {
                string color = string.IsNullOrEmpty(entry.PrefixColor) ? "#F5E945" : entry.PrefixColor;
                string formattedPrefix = $"<font color=\"{color}\"><strong>{entry.Prefix}</strong></font>";
                finalMessage = $"{formattedPrefix} {finalMessage}";
            }

            player.SendMessage(0, finalMessage, EnumChatType.Notification);
        }

        private void ProcessMatch(string category, string logFilePath, IServerPlayer player, string message, string keyword)
        {
            if (config.CreateServerLogs)
            {
                LogToFile(logFilePath, player, message);
            }

            if (config.SendLogsToDiscord && !string.IsNullOrEmpty(config.DiscordWebhookUrl))
            {
                Task.Run(() => SendToDiscord(category, player, message, keyword));
            }
        }

        private void LogToFile(string path, IServerPlayer player, string message)
        {
            try
            {
                if (!Directory.Exists(chatEyeLogDir)) Directory.CreateDirectory(chatEyeLogDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string entry = $"[{timestamp} | PlayerUID: {player.PlayerUID}] {player.PlayerName}: {message}";
                File.AppendAllLines(path, new[] { entry });
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[ChatEye] Log Error: " + ex.Message);
            }
        }

        private async Task SendToDiscord(string category, IServerPlayer player, string message, string keyword)
        {
            try
            {
                string safeName = EscapeJson(player.PlayerName);
                string safeMsg = EscapeJson(message);
                string safeUid = EscapeJson(player.PlayerUID);
                string safeKeyword = EscapeJson(keyword);
                
                int color = (category == "OBSCENE") ? 16711680 : 3447003;

                string jsonPayload = $@"
                {{
                    ""embeds"": [
                        {{
                            ""title"": ""ChatEye Trigger: {category}"",
                            ""color"": {color},
                            ""fields"": [
                                {{ ""name"": ""Player"", ""value"": ""{safeName}"", ""inline"": true }},
                                {{ ""name"": ""Trigger"", ""value"": ""{safeKeyword}"", ""inline"": true }},
                                {{ ""name"": ""UID"", ""value"": ""{safeUid}"", ""inline"": false }},
                                {{ ""name"": ""Message"", ""value"": ""{safeMsg}"" }}
                            ],
                            ""footer"": {{ ""text"": ""Vintage Story ChatEye Mod"" }}
                        }}
                    ]
                }}";

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                await client.PostAsync(config.DiscordWebhookUrl, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ChatEye] Discord Error: " + ex.Message);
            }
        }

        private string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
