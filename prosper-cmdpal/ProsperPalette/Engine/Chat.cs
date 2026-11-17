#nullable disable
// Typed requests and follow-ups, as Prosper's history window did them: continue the OpenAI conversation by id while
// OpenAI still has it, otherwise send the earlier conversation as text. An audio file can come along (transcribed
// first); sent without a question, its transcript is the answer. Call off the UI thread.
using System;
using System.IO;

namespace Prosper
{
    class ChatOutcome
    {
        public Conversation Conversation;
        public AssistResult Result;
        public string Error;
    }

    static class Chat
    {
        public const long MaxAudio = 25L * 1024 * 1024; // OpenAI's upload limit

        /// <summary>
        /// Sends [text] (and/or the audio file at [audioPath]) in conversation [id] (null starts one) and saves both
        /// sides to history. [where] tells the Assistant where its answer will appear. Never throws.
        /// </summary>
        public static ChatOutcome Ask(string id, string text, string audioPath, string where, string kind, Action<string> onStep)
        {
            text = (text ?? "").Trim();
            var s = Settings.Current;
            var conv = History.Get(id);
            string audioName = null, audioText = null;
            AssistResult result = null; string error = null;
            try
            {
                if (!s.Configured) throw new Exception("Add your OpenAI API key first: Command Palette → Prosper settings.");
                if (!string.IsNullOrWhiteSpace(audioPath))
                {
                    audioPath = audioPath.Trim().Trim('"');
                    var f = new FileInfo(audioPath);
                    if (!f.Exists) throw new Exception("No audio file at " + audioPath);
                    if (f.Length > MaxAudio) throw new Exception("That file is over 25 MB, OpenAI's limit. Trim or compress it first.");
                    if (onStep != null) onStep("Transcribing " + f.Name);
                    audioText = Ai.Transcribe(f.FullName);
                    audioName = f.Name;
                }
                if (text.Length == 0 && audioName == null) throw new Exception("Type a request first.");
                if (text.Length > 0)
                {
                    if (onStep != null) onStep("Thinking");
                    var ctx = new AssistContext
                    {
                        App = where,
                        AboutMe = s.AboutMe,
                        Audio = audioName != null ? "File: " + audioName + "\n\n" + audioText : null,
                    };
                    bool useMcp = s.McpOn && s.McpServer.Length > 0;
                    string token = useMcp ? Mcp.AccessToken() : null;
                    if (conv != null && conv.ResponseId == null) ctx.Earlier = History.Transcript(conv);
                    try { result = Ai.RunAssistant(text, ctx, useMcp, token, conv != null ? conv.ResponseId : null, onStep, Engine.Confirm, null); }
                    catch (Exception x)
                    {
                        // OpenAI keeps a conversation for a while only: continue from the text when it has gone.
                        if (conv == null || conv.ResponseId == null || Unwrap(x).IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0) throw;
                        ctx.Earlier = History.Transcript(conv);
                        result = Ai.RunAssistant(text, ctx, useMcp, token, null, onStep, Engine.Confirm, null);
                    }
                }
            }
            catch (Exception x) { error = Unwrap(x); }
            var saved = History.Add(conv != null ? conv.Id : null, kind, "Command Palette", text, result, error, null, audioName, audioText);
            Engine.LastConversationId = saved.Id;
            return new ChatOutcome { Conversation = saved, Result = result, Error = error };
        }

        public static string Unwrap(Exception x)
        {
            while (x is AggregateException && x.InnerException != null) x = x.InnerException;
            return x.Message;
        }
    }
}
