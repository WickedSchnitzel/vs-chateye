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
                sapi.StoreModConfig(config, "ChatEyeConfig.json");
            }

            // Pfade
            string baseLogDir = api.GetOrCreateDataPath("Logs");
            chatEyeLogDir = Path.Combine(baseLogDir, "ChatEyeLogs");
            generalLogPath = Path.Combine(chatEyeLogDir, config.GeneralLogName);
            obsceneLogPath = Path.Combine(chatEyeLogDir, config.ObsceneLogName);

            // Ordner erstellen, falls lokales Logging aktiv ist
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

            string lowerMsg = cleanMessage.ToLowerInvariant();
            string spacelessMsg = lowerMsg.Replace(" ", "");

            // 1. Obscene Check
            if (config.ObsceneKeywords != null)
            {
                foreach (var word in config.ObsceneKeywords)
                {
                    if (string.IsNullOrWhiteSpace(word)) continue;
                    string lowerWord = word.ToLowerInvariant();
                    string spacelessWord = lowerWord.Replace(" ", "");

                    if (lowerMsg.Contains(lowerWord) || spacelessMsg.Contains(spacelessWord))
                    {
                        ProcessMatch("OBSCENE", obsceneLogPath, player, cleanMessage, word);
                        return; 
                    }
                }
            }

            // 2. General Check
            if (config.GeneralKeywords != null)
            {
                foreach (var word in config.GeneralKeywords)
                {
                    if (string.IsNullOrWhiteSpace(word)) continue;
                    string lowerWord = word.ToLowerInvariant();
                    string spacelessWord = lowerWord.Replace(" ", "");

                    if (lowerMsg.Contains(lowerWord) || spacelessMsg.Contains(spacelessWord))
                    {
                        ProcessMatch("GENERAL", generalLogPath, player, cleanMessage, word);
                        return; 
                    }
                }
            }
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
                                {{ ""name"": ""Keyword"", ""value"": ""{safeKeyword}"", ""inline"": true }},
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