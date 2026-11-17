// Prosper settings as a Command Palette form. Values go to Prosper's own settings file, with the API key and MCP
// tokens sealed by Windows DPAPI for this account (never stored or shown in plain text), exactly as the app did.
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Prosper;

namespace ProsperPalette;

internal sealed partial class SettingsPage : ContentPage
{
    private readonly SettingsForm _form = new();

    public SettingsPage()
    {
        Id = "prosper.settings";
        Name = "Open";
        Title = "Prosper settings";
        Icon = Icons.Settings;
    }

    public override IContent[] GetContent()
    {
        _form.Refresh(null);
        return [_form];
    }

    private sealed partial class SettingsForm : FormContent
    {
        private const string DefaultBase = "https://api.openai.com/v1";

        public SettingsForm()
        {
            TemplateJson = """
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.5",
              "body": [
                { "type": "TextBlock", "text": "${status}", "wrap": true, "color": "${statusColor}", "weight": "bolder", "isVisible": "${hasStatus}" },
                { "type": "TextBlock", "text": "OpenAI", "size": "medium", "weight": "bolder", "style": "heading" },
                { "type": "Input.Text", "id": "apiKey", "label": "API key", "style": "password", "placeholder": "${keyHint}" },
                { "type": "Input.Text", "id": "apiBase", "label": "Endpoint", "value": "${apiBase}", "placeholder": "https://api.openai.com/v1, or your Azure OpenAI / compatible endpoint" },
                { "type": "ColumnSet", "columns": [
                  { "type": "Column", "items": [ { "type": "Input.Text", "id": "model", "label": "Assistant model", "value": "${model}" } ] },
                  { "type": "Column", "items": [ { "type": "Input.Text", "id": "transcribeModel", "label": "Speech-to-text model", "value": "${transcribeModel}" } ] }
                ] },
                { "type": "Input.Text", "id": "aboutMe", "label": "About you (the Assistant uses it when writing for you)", "isMultiline": true, "value": "${aboutMe}", "placeholder": "Name, role, how you like to sign emails…" },
                { "type": "TextBlock", "text": "Prosper", "size": "medium", "weight": "bolder", "style": "heading", "spacing": "large" },
                { "type": "Input.Toggle", "id": "cleanup", "title": "Clean up dictation (no ums, proper punctuation)", "value": "${cleanup}" },
                { "type": "Input.Toggle", "id": "keepHistory", "title": "Keep history on this PC (encrypted for your Windows account)", "value": "${keepHistory}" },
                { "type": "Input.Toggle", "id": "showBubble", "title": "Keep the Prosper bar on screen when idle (it always shows while recording)", "value": "${showBubble}" },
                { "type": "Input.Toggle", "id": "globalShortcuts", "title": "Global shortcuts: Ctrl+Alt+Space dictate · Ctrl+Alt+A Assistant · Ctrl+Alt+S add screen", "value": "${globalShortcuts}" },
                { "type": "TextBlock", "text": "MCP tools (optional)", "size": "medium", "weight": "bolder", "style": "heading", "spacing": "large" },
                { "type": "TextBlock", "text": "${mcpState}", "wrap": true, "isSubtle": true },
                { "type": "Input.Toggle", "id": "mcpOn", "title": "Let the Assistant use my MCP server", "value": "${mcpOn}" },
                { "type": "Input.Text", "id": "mcpServer", "label": "Server URL", "value": "${mcpServer}", "placeholder": "https://…" },
                { "type": "Input.Text", "id": "mcpToken", "label": "Access token (only for servers without sign-in)", "style": "password", "placeholder": "${tokenHint}" },
                { "type": "Input.Text", "id": "mcpAllowed", "label": "Always allowed tools (comma separated; every other tool asks first)", "value": "${mcpAllowed}" }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "Save", "style": "positive", "data": { "action": "save" } },
                { "type": "Action.Submit", "title": "Test connection", "data": { "action": "test" } },
                { "type": "Action.Submit", "title": "Sign in to MCP server", "data": { "action": "signin" } },
                { "type": "Action.Submit", "title": "Sign out of MCP", "data": { "action": "signout" } }
              ]
            }
            """;
        }

        /// <summary>Re-renders the form from the saved settings, with an optional status line on top.</summary>
        public void Refresh(string? status, bool error = false)
        {
            var s = Prosper.Settings.Current;
            var d = new JsonObject
            {
                ["status"] = status ?? string.Empty,
                ["hasStatus"] = status != null,
                ["statusColor"] = error ? "attention" : "good",
                ["keyHint"] = s.Configured ? "Saved and encrypted. Paste a new key to replace it." : "Paste your OpenAI key (sk-…)",
                ["apiBase"] = s.ApiBase,
                ["model"] = s.Model,
                ["transcribeModel"] = s.TranscribeModel,
                ["aboutMe"] = s.AboutMe,
                ["cleanup"] = s.Cleanup ? "true" : "false",
                ["keepHistory"] = s.KeepHistory ? "true" : "false",
                ["showBubble"] = s.ShowBubble ? "true" : "false",
                ["globalShortcuts"] = s.GlobalShortcuts ? "true" : "false",
                ["mcpOn"] = s.McpOn ? "true" : "false",
                ["mcpServer"] = s.McpServer,
                ["mcpAllowed"] = s.McpAllowed,
                ["tokenHint"] = Secret.OpenText(s.McpManualSealed).Length > 0 ? "A token is saved. Paste a new one to replace it." : "Paste a token",
                ["mcpState"] = string.IsNullOrEmpty(s.McpServer) ? "Not connected: the Assistant only writes text."
                    : Mcp.SignedIn ? "Connected to " + s.McpServer + (s.McpOn ? "." : " (turned off).")
                    : "Not signed in to " + s.McpServer + ". Sign in, or paste a token.",
            };
            DataJson = d.ToJsonString();
        }

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            string action = "save";
            try { action = (JsonNode.Parse(data) as JsonObject)?["action"]?.GetValue<string>() ?? "save"; }
            catch (JsonException) { }

            JsonObject? i = null;
            try { i = JsonNode.Parse(inputs) as JsonObject; }
            catch (JsonException) { }

            Apply(i);

            switch (action)
            {
                case "test":
                    Refresh("Testing…");
                    Task.Run(() =>
                    {
                        try { Refresh(Ai.Test()); }
                        catch (Exception x) { Refresh(Chat.Unwrap(x), true); }
                    });
                    break;
                case "signin":
                    if (!Prosper.Settings.Current.McpServer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        Refresh("Enter the MCP server's https:// address first.", true);
                        break;
                    }

                    Refresh("Finish signing in in your browser…");
                    Task.Run(() =>
                    {
                        try
                        {
                            Mcp.SignIn();
                            Prosper.Settings.Current.McpOn = true;
                            Prosper.Settings.Save();
                            Refresh("Connected.");
                        }
                        catch (Exception x) { Refresh(Chat.Unwrap(x), true); }
                    });
                    break;
                case "signout":
                    Mcp.SignOut();
                    Refresh("Signed out of the MCP server.");
                    break;
                default:
                    Refresh("Saved.");
                    break;
            }

            return CommandResult.KeepOpen();
        }

        private static string Str(JsonObject? i, string key)
        {
            try { return i?[key]?.GetValue<string>()?.Trim() ?? string.Empty; }
            catch (InvalidOperationException) { return i?[key]?.ToString().Trim() ?? string.Empty; }
        }

        private static bool On(JsonObject? i, string key) => string.Equals(Str(i, key), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>Prosper's own rules: keys only over https, blank fields fall back to the defaults.</summary>
        private static void Apply(JsonObject? i)
        {
            if (i == null) return;
            var s = Prosper.Settings.Current;
            var key = Str(i, "apiKey");
            if (key.Length > 0) s.SetApiKey(key);
            var apiBase = Str(i, "apiBase").TrimEnd('/');
            s.ApiBase = apiBase.Length > 0 && apiBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? apiBase : DefaultBase;
            var model = Str(i, "model");
            s.Model = model.Length > 0 ? model : "gpt-6-luna";
            var transcribe = Str(i, "transcribeModel");
            s.TranscribeModel = transcribe.Length > 0 ? transcribe : "gpt-4o-mini-transcribe";
            s.AboutMe = Str(i, "aboutMe");
            s.Cleanup = On(i, "cleanup");
            s.KeepHistory = On(i, "keepHistory");
            s.ShowBubble = On(i, "showBubble");
            s.GlobalShortcuts = On(i, "globalShortcuts");
            var server = Str(i, "mcpServer");
            if (server != s.McpServer)
            {
                s.ClearMcp(); // new server, new sign-in
                s.McpServer = server;
            }

            var token = Str(i, "mcpToken");
            if (token.Length > 0) s.McpManualSealed = Secret.SealText(token);
            s.McpAllowed = Str(i, "mcpAllowed");
            s.McpOn = On(i, "mcpOn") && s.McpServer.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            Prosper.Settings.Save();
            Engine.ApplySettings();
        }
    }
}
