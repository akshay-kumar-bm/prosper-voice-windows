// Prosper's commands in PowerToys Command Palette:
//   Dictate · Assistant (voice; bind them to keys in Command Palette settings, or use Ctrl+Alt+Space / Ctrl+Alt+A)
//   Ask Prosper (typed) · Prosper history · Prosper settings · "Ask Prosper: …" for anything typed in the search box
//   a Dock band: Dictate · Assistant · Add screen · Cancel, showing the timer while recording.
using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Prosper;

namespace ProsperPalette;

public sealed partial class ProsperCommandsProvider : CommandProvider
{
    private const string DictateHint = "Speak into the app you're typing in · with text selected, say how to change it";
    private const string AssistantHint = "Ask for an email, a reply, an answer… it sees your selection, and your screen if you add it";

    private readonly CommandItem _dictate;
    private readonly CommandItem _assistant;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks = [new AskFallback()];

    private readonly ListItem _dockDictate;
    private readonly ListItem _dockAssistant;
    private readonly ListItem _dockScreen;
    private readonly ListItem _dockCancel;
    private readonly WrappedDockItem _band;
    private readonly System.Threading.Timer _tick;

    public ProsperCommandsProvider()
    {
        Id = "prosper.palette";
        DisplayName = "Prosper";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        var settings = new SettingsPage();
        _dictate = new CommandItem(new VoiceCommand(false))
        {
            Title = "Prosper: Dictate",
            Subtitle = DictateHint,
            MoreCommands = VoiceMore(settings),
        };
        _assistant = new CommandItem(new VoiceCommand(true))
        {
            Title = "Prosper: Assistant",
            Subtitle = AssistantHint,
            MoreCommands = VoiceMore(settings),
        };
        _commands =
        [
            _dictate,
            _assistant,
            new CommandItem(new AskPage()) { Title = "Ask Prosper", Subtitle = "Type a request: answer here, or write it into your app" },
            new CommandItem(new ConversationPage(null) { Id = "prosper.chat", Name = "Open" })
            {
                Title = "Prosper: Chat or transcribe audio",
                Subtitle = "A typed chat; attach an audio file to transcribe it or ask about it",
                Icon = Icons.Audio,
            },
            new CommandItem(new HistoryPage()) { Title = "Prosper history", Subtitle = "Everything you said and Prosper answered, with follow-ups" },
            new CommandItem(settings) { Title = "Prosper settings", Subtitle = "OpenAI key and models, About you, MCP server" },
        ];

        _dockDictate = new ListItem(new VoiceCommand(false)) { Title = "Dictate", Icon = Icons.Mic };
        _dockAssistant = new ListItem(new VoiceCommand(true)) { Title = "Assistant", Icon = Icons.Assistant };
        _dockScreen = new ListItem(new AddScreenCommand()) { Title = string.Empty, Icon = Icons.Screen, Subtitle = "Add screen" };
        _dockCancel = new ListItem(new CancelCommand()) { Title = string.Empty, Icon = Icons.Cancel, Subtitle = "Cancel" };
        _band = new WrappedDockItem([_dockDictate, _dockAssistant, _dockScreen, _dockCancel], "prosper.dock", "Prosper")
        {
            Icon = Icons.Mic,
        };

        // The engine owns the microphone, the global shortcuts and the Prosper pill: start it with the extension.
        Engine.Changed += UpdateState;
        Engine.Start();
        _tick = new System.Threading.Timer(_ => { if (Engine.Mode == EngineMode.Recording) UpdateState(); }, null, 1000, 1000);
    }

    private static IContextItem[] VoiceMore(SettingsPage settings) =>
    [
        new CommandContextItem(new FinishCommand()),
        new CommandContextItem(new CancelCommand()),
        new CommandContextItem(new AddScreenCommand()),
        new CommandContextItem(settings) { Title = "Prosper settings" },
    ];

    private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>Reflects what Prosper is doing in the command titles and the Dock.</summary>
    private void UpdateState()
    {
        var mode = Engine.Mode;
        bool assist = Engine.Assistant;
        string clock = Clock(Engine.Elapsed);
        switch (mode)
        {
            case EngineMode.Recording:
                var live = "● " + clock + (assist && Engine.ScreenAdded ? " · screen added" : string.Empty) + " · run again to finish";
                _dictate.Subtitle = assist ? DictateHint : "Recording " + live;
                _assistant.Subtitle = assist ? "Listening " + live : AssistantHint;
                _dockDictate.Title = assist ? "Dictate" : "● " + clock;
                _dockDictate.Icon = assist ? Icons.Mic : Icons.Recording;
                _dockAssistant.Title = assist ? "● " + clock : "Assistant";
                _dockAssistant.Icon = assist ? Icons.Recording : Icons.Assistant;
                break;
            case EngineMode.Working:
                var step = (Engine.Step ?? "Working") + "…";
                _dictate.Subtitle = assist ? DictateHint : step;
                _assistant.Subtitle = assist ? step : AssistantHint;
                _dockDictate.Title = assist ? "Dictate" : step;
                _dockDictate.Icon = assist ? Icons.Mic : Icons.Working;
                _dockAssistant.Title = assist ? step : "Assistant";
                _dockAssistant.Icon = assist ? Icons.Working : Icons.Assistant;
                break;
            default:
                _dictate.Subtitle = DictateHint;
                _assistant.Subtitle = AssistantHint;
                _dockDictate.Title = "Dictate";
                _dockDictate.Icon = Icons.Mic;
                _dockAssistant.Title = "Assistant";
                _dockAssistant.Icon = Icons.Assistant;
                break;
        }
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;

    public override ICommandItem[] GetDockBands() => [_band];

    public override void Dispose()
    {
        _tick.Dispose();
        Engine.Changed -= UpdateState;
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
