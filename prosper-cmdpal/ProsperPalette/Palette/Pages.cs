// Prosper history and typed requests in Command Palette.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Prosper;

namespace ProsperPalette;

/// <summary>Every conversation on this PC, newest first, with a preview; open one to read it all and follow up.</summary>
internal sealed partial class HistoryPage : ListPage
{
    public HistoryPage()
    {
        Id = "prosper.history";
        Name = "Open";
        Title = "Prosper history";
        Icon = Icons.History;
        PlaceholderText = "Search what you said and what Prosper answered";
        ShowDetails = true;
        History.Changed += () => RaiseItemsChanged();
        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "No conversations yet",
            Subtitle = "Dictate, use the Assistant or ask Prosper: everything shows up here.",
            Icon = Icons.History,
        };
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();
        foreach (var c in History.All())
        {
            var id = c.Id;
            Func<string?> answer = () => Md.LastAnswer(History.Get(id));
            items.Add(new ListItem(new ConversationPage(id))
            {
                Title = string.IsNullOrWhiteSpace(c.Title) ? "(nothing heard)" : c.Title,
                Subtitle = string.IsNullOrWhiteSpace(c.Summary) ? Md.When(c.UpdatedAt) : c.Summary,
                Icon = c.Kind switch { "dictation" => Icons.Mic, "rewrite" => Icons.Type, "chat" => Icons.Chat, _ => Icons.Assistant },
                Tags = [new Tag(Md.Kind(c.Kind))],
                Details = new Details
                {
                    Title = Md.Kind(c.Kind) + (string.IsNullOrEmpty(c.App) ? string.Empty : " · " + c.App) + " · " + Md.When(c.CreatedAt),
                    Body = Md.Conversation(c, images: false),
                },
                MoreCommands =
                [
                    new CommandContextItem(new CopyCommand(answer, "Copy answer")),
                    new CommandContextItem(new InsertCommand(answer)),
                    new CommandContextItem(new DeleteConversationCommand(id)) { IsCritical = true },
                ],
            });
        }

        if (items.Count > 0)
        {
            items.Add(new ListItem(new ClearHistoryCommand()) { Title = "Clear history", Subtitle = "Delete every conversation saved on this PC" });
        }

        return items.ToArray();
    }
}

/// <summary>Type a request: see the answer in the palette, or have it written straight into the app you came from.</summary>
internal sealed partial class AskPage : DynamicListPage
{
    private string _query = string.Empty;

    public AskPage()
    {
        Id = "prosper.ask";
        Name = "Open";
        Title = "Ask Prosper";
        Icon = Icons.Chat;
        PlaceholderText = "Write a reply to… / Summarise… / What does… mean?";
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _query = newSearch ?? string.Empty;
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        var q = _query.Trim();
        if (q.Length == 0)
        {
            return
            [
                new ListItem(new ConversationPage(null)) { Title = "New chat", Subtitle = "Type, follow up, or attach an audio file", Icon = Icons.Chat },
                new ListItem(new VoiceCommand(true)) { Title = "Ask by voice instead", Subtitle = "The Assistant, with your selection and (optionally) your screen", Icon = Icons.Assistant },
            ];
        }

        var app = Engine.LastAppName;
        return
        [
            new ListItem(new ConversationPage(null, q)) { Title = "Ask: " + q, Subtitle = "See the answer here, then copy or insert it", Icon = Icons.Chat },
            new ListItem(new AskAndTypeCommand(q))
            {
                Title = "Write into " + (string.IsNullOrEmpty(app) ? "the app" : app) + ": " + q,
                Subtitle = "The answer is typed where your cursor was",
                Icon = Icons.Type,
            },
        ];
    }
}

/// <summary>"Ask Prosper: …" for anything typed in Command Palette's search box.</summary>
internal sealed partial class AskFallback : FallbackCommandItem
{
    public AskFallback()
        : base("Ask Prosper", "prosper.fallback.ask")
    {
        Title = string.Empty;
        Subtitle = "Prosper Assistant";
        Icon = Icons.Assistant;
    }

    public override void UpdateQuery(string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length < 3)
        {
            Title = string.Empty;
            return;
        }

        Title = "Ask Prosper: " + q;
        Command = new ConversationPage(null, q) { Id = "prosper.fallback.ask.page", Name = "Ask" };
    }
}
