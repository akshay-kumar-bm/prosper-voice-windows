// A Prosper conversation inside Command Palette: what was said and answered (Markdown, tool calls and screenshots
// included) and a form to follow up by typing, optionally with an audio file to transcribe or ask about. Opened with
// a question, it asks it as soon as it is shown: that is how typed requests and the "Ask Prosper" fallback answer.
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Prosper;

namespace ProsperPalette;

internal sealed partial class ConversationPage : ContentPage
{
    private const string Where = "Command Palette (your answer is shown there in a chat, with buttons to copy it or insert it into the app the user came from)";

    private readonly MarkdownContent _view = new();
    private readonly FollowUpForm _form;
    private readonly object _gate = new();
    private string? _id;
    private string? _pending;   // a question to ask when the page is first shown
    private string? _step;
    private bool _busy;

    /// <param name="id">An existing conversation, or null for a new chat.</param>
    /// <param name="ask">A question to ask straight away.</param>
    public ConversationPage(string? id, string? ask = null)
    {
        _id = id;
        _pending = string.IsNullOrWhiteSpace(ask) ? null : ask.Trim();
        _form = new FollowUpForm(this);
        Icon = Icons.Chat;
        Name = id != null ? "Open" : "Ask";
        Id = id != null ? "prosper.conversation." + id : string.Empty;
        var c = History.Get(id);
        Title = c != null ? (string.IsNullOrWhiteSpace(c.Title) ? "Prosper" : c.Title) : _pending ?? "Prosper chat";
        Commands =
        [
            new CommandContextItem(new CopyCommand(Answer, "Copy answer")),
            new CommandContextItem(new InsertCommand(Answer)),
        ];
        Render();
    }

    private string? Answer() => Md.LastAnswer(History.Get(_id));

    public override IContent[] GetContent()
    {
        string? ask;
        lock (_gate)
        {
            ask = _pending;
            _pending = null;
        }

        if (ask != null) Send(ask, null);
        return [_view, _form];
    }

    private void Render()
    {
        var c = History.Get(_id);
        string body;
        if (c == null && !_busy)
        {
            body = "### Ask Prosper\n\nType a request below: an email, a reply, an answer, or something for your MCP tools to do.\n\n" +
                "Add the path of an audio file (up to 25 MB) to get its transcript, or to ask about it.";
        }
        else
        {
            body = Md.Conversation(c);
        }

        if (_busy)
        {
            body += (body.Length > 0 ? "\n\n" : string.Empty) + "*" + (_step ?? "Thinking") + "…*";
        }

        _view.Body = body;
    }

    /// <summary>Sends a request (and/or an audio file) in this conversation, in the background.</summary>
    public void Send(string? text, string? audio)
    {
        text = (text ?? string.Empty).Trim();
        audio = string.IsNullOrWhiteSpace(audio) ? null : audio.Trim();
        lock (_gate)
        {
            if (_busy || (text.Length == 0 && audio == null)) return;
            _busy = true;
        }

        if (text.Length > 0 && History.Get(_id) == null)
        {
            // A new chat shows the question at once, before the answer arrives.
            _view.Body = "**You**\n\n> " + text.Replace("\n", "\n> ") + "\n\n*Thinking…*";
        }
        else
        {
            Render();
        }

        IsLoading = true;
        Task.Run(() =>
        {
            var outcome = Chat.Ask(_id, text, audio, Where, "chat", step =>
            {
                _step = step;
                Render();
            });
            _id = outcome.Conversation.Id;
            _busy = false;
            _step = null;
            IsLoading = false;
            Title = string.IsNullOrWhiteSpace(outcome.Conversation.Title) ? Title : outcome.Conversation.Title;
            Render();
            if (outcome.Error != null)
            {
                ExtensionHost.ShowStatus(new StatusMessage { Message = outcome.Error, State = MessageState.Error }, StatusContext.Page);
            }
        });
    }

    /// <summary>The follow-up box under the conversation.</summary>
    private sealed partial class FollowUpForm : FormContent
    {
        private readonly ConversationPage _page;

        public FollowUpForm(ConversationPage page)
        {
            _page = page;
            TemplateJson = """
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.5",
              "body": [
                { "type": "Input.Text", "id": "ask", "isMultiline": true, "placeholder": "Ask Prosper, or follow up…" },
                { "type": "Input.Text", "id": "audio", "placeholder": "Optional: path of an audio file to transcribe or ask about (mp3, m4a, wav, webm…)" }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "Send", "data": { "action": "send" } }
              ]
            }
            """;
        }

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            string? ask = null, audio = null;
            try
            {
                var o = JsonNode.Parse(inputs) as JsonObject;
                ask = o?["ask"]?.GetValue<string>();
                audio = o?["audio"]?.GetValue<string>();
            }
            catch (JsonException)
            {
            }

            if (string.IsNullOrWhiteSpace(ask) && string.IsNullOrWhiteSpace(audio))
            {
                return CommandResult.ShowToast(new ToastArgs { Message = "Type a request first.", Result = CommandResult.KeepOpen() });
            }

            _page.Send(ask, audio);

            // Re-render the card so the box is empty for the next follow-up.
            var t = TemplateJson;
            TemplateJson = t + " ";
            return CommandResult.KeepOpen();
        }
    }
}
