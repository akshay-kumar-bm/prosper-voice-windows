// The things Prosper does, as Command Palette commands. Voice commands dismiss the palette first, so the app the
// user was typing in is back in front when the microphone starts and the text is pasted.
using System;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Prosper;

namespace ProsperPalette;

internal static class Icons
{
    public static readonly IconInfo Mic = new("");
    public static readonly IconInfo Assistant = new("");
    public static readonly IconInfo Screen = new("");
    public static readonly IconInfo Cancel = new("");
    public static readonly IconInfo Finish = new("");
    public static readonly IconInfo Chat = new("");
    public static readonly IconInfo History = new("");
    public static readonly IconInfo Settings = new("");
    public static readonly IconInfo Copy = new("");
    public static readonly IconInfo Paste = new("");
    public static readonly IconInfo Delete = new("");
    public static readonly IconInfo Audio = new("");
    public static readonly IconInfo Type = new("");
    public static readonly IconInfo Recording = new("");
    public static readonly IconInfo Working = new("");
}

/// <summary>Dictate (or the Assistant): starts recording, or finishes the recording in progress.</summary>
internal sealed partial class VoiceCommand : InvokableCommand
{
    private readonly bool _assistant;

    public VoiceCommand(bool assistant)
    {
        _assistant = assistant;
        Id = assistant ? "prosper.assistant" : "prosper.dictate";
        Name = assistant ? "Assistant" : "Dictate";
        Icon = assistant ? Icons.Assistant : Icons.Mic;
    }

    public override ICommandResult Invoke()
    {
        Engine.Toggle(_assistant);
        return CommandResult.Dismiss();
    }
}

internal sealed partial class FinishCommand : InvokableCommand
{
    public FinishCommand()
    {
        Id = "prosper.finish";
        Name = "Finish recording";
        Icon = Icons.Finish;
    }

    public override ICommandResult Invoke()
    {
        Engine.Finish();
        return CommandResult.Dismiss();
    }
}

internal sealed partial class CancelCommand : InvokableCommand
{
    public CancelCommand()
    {
        Id = "prosper.cancel";
        Name = "Cancel recording";
        Icon = Icons.Cancel;
    }

    public override ICommandResult Invoke()
    {
        Engine.Cancel();
        return CommandResult.Dismiss();
    }
}

/// <summary>Adds (or removes) the screen to the Assistant request being recorded.</summary>
internal sealed partial class AddScreenCommand : InvokableCommand
{
    public AddScreenCommand()
    {
        Id = "prosper.screen";
        Name = "Add screen";
        Icon = Icons.Screen;
    }

    public override ICommandResult Invoke()
    {
        if (Engine.Mode != EngineMode.Recording || !Engine.Assistant)
        {
            return CommandResult.ShowToast("Start the Assistant first, then add the screen while you talk.");
        }

        Engine.AddScreen();
        return CommandResult.Dismiss();
    }
}

/// <summary>Copies text, kept out of cloud clipboard sync (Prosper's own clipboard handling).</summary>
internal sealed partial class CopyCommand : InvokableCommand
{
    private readonly Func<string?> _text;

    public CopyCommand(Func<string?> text, string name = "Copy")
    {
        _text = text;
        Name = name;
        Icon = Icons.Copy;
    }

    public override ICommandResult Invoke()
    {
        var t = _text();
        if (string.IsNullOrEmpty(t)) return CommandResult.ShowToast("Nothing to copy yet.");
        Engine.Copy(t);
        return CommandResult.ShowToast("Copied");
    }
}

/// <summary>Pastes text into the app the user was in before opening Command Palette.</summary>
internal sealed partial class InsertCommand : InvokableCommand
{
    private readonly Func<string?> _text;

    public InsertCommand(Func<string?> text)
    {
        _text = text;
        var app = Engine.LastAppName;
        Name = string.IsNullOrEmpty(app) ? "Insert into the app" : "Insert into " + app;
        Icon = Icons.Paste;
    }

    public override ICommandResult Invoke()
    {
        var t = _text();
        if (string.IsNullOrEmpty(t)) return CommandResult.ShowToast("Nothing to insert yet.");
        Engine.InsertIntoApp(t);
        return CommandResult.Dismiss();
    }
}

/// <summary>
/// A typed Assistant request whose answer is typed straight into the app the user was in (like the voice Assistant,
/// without speaking). Progress shows in the Prosper pill.
/// </summary>
internal sealed partial class AskAndTypeCommand : InvokableCommand
{
    private readonly string _request;

    public AskAndTypeCommand(string request)
    {
        _request = request;
        Name = "Write it into the app";
        Icon = Icons.Type;
    }

    public override ICommandResult Invoke()
    {
        if (!Prosper.Settings.Current.Configured) return CommandResult.ShowToast("Add your OpenAI API key in Prosper settings first.");
        var app = Engine.LastAppName ?? "an app";
        var request = _request;
        Task.Run(() =>
        {
            Engine.ShowWorking("Thinking");
            var o = Chat.Ask(null, request, null, app + " (a text field is focused: your answer is typed into it)", "assistant", step => Engine.ShowWorking(step));
            Engine.HideWorking();
            if (o.Error != null) { Engine.Say(o.Error, 6000, true); return; }
            if (o.Result!.IsStatus) { Engine.Copy(o.Result.Text); Engine.Say(o.Result.Text, 6000); return; }
            Engine.Copy(o.Result.Text);
            Engine.InsertIntoApp(o.Result.Text);
        });
        return CommandResult.Dismiss();
    }
}

internal sealed partial class DeleteConversationCommand : InvokableCommand
{
    private readonly string _id;

    public DeleteConversationCommand(string id)
    {
        _id = id;
        Name = "Delete";
        Icon = Icons.Delete;
    }

    public override ICommandResult Invoke()
    {
        History.Delete(_id);
        return CommandResult.KeepOpen();
    }
}

internal sealed partial class ClearHistoryCommand : InvokableCommand
{
    public ClearHistoryCommand()
    {
        Name = "Clear history";
        Icon = Icons.Delete;
    }

    public override ICommandResult Invoke() => CommandResult.Confirm(new ConfirmationArgs
    {
        Title = "Clear Prosper history?",
        Description = "Every conversation saved on this PC (screenshots included) is deleted. This can't be undone.",
        IsPrimaryCommandCritical = true,
        PrimaryCommand = new AnonymousCommand(History.Clear) { Name = "Clear history", Result = CommandResult.GoHome() },
    });
}
