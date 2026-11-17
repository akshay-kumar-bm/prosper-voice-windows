// Prosper's conversations as Markdown, which Command Palette renders in content pages and details panes.
using System;
using System.Linq;
using System.Text;
using Prosper;

namespace ProsperPalette;

internal static class Md
{
    public static string Kind(string? kind) => kind switch
    {
        "dictation" => "Dictation",
        "rewrite" => "Voice edit",
        "chat" => "Chat",
        _ => "Assistant",
    };

    public static string When(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The last answer in a conversation (what Copy / Insert act on), or null.</summary>
    public static string? LastAnswer(Conversation? c) =>
        c?.Messages.LastOrDefault(m => m.Role == "assistant" && !string.IsNullOrWhiteSpace(m.Text))?.Text
        ?? c?.Messages.LastOrDefault(m => m.Role == "audio")?.Response;

    static string Fence(string s, int max)
    {
        s ??= string.Empty;
        if (s.Length > max) s = s[..max] + "…";
        return "```\n" + s.Replace("```", "ʼʼʼ") + "\n```\n";
    }

    static string Quote(string s) => "> " + (s ?? string.Empty).Trim().Replace("\r", string.Empty).Replace("\n", "\n> ");

    /// <param name="images">Include screenshots (data: URIs); left out in small details panes.</param>
    public static string Conversation(Conversation? c, bool images = true)
    {
        if (c == null) return "*Nothing here yet.*";
        var b = new StringBuilder();
        foreach (var m in c.Messages)
        {
            switch (m.Role)
            {
                case "user":
                    b.Append("**You**\n\n");
                    if (images && m.Image != null) b.Append("![Your screen](data:image/jpeg;base64,").Append(m.Image).Append(")\n\n");
                    b.Append(Quote(string.IsNullOrWhiteSpace(m.Text) ? "(nothing heard)" : m.Text)).Append("\n\n");
                    break;
                case "assistant":
                    b.Append(m.Status ? "**Prosper** · result\n\n" : "**Prosper**\n\n").Append(m.Text).Append("\n\n---\n\n");
                    break;
                case "tool":
                    b.Append(m.Ok ? "✓ " : "✗ ").Append('`').Append(m.Text.Replace("`", "'")).Append('`');
                    if (m.Ms > 0) b.Append(" · ").Append(m.Ms).Append(" ms");
                    b.Append("\n\n");
                    if (!string.IsNullOrEmpty(m.Request)) b.Append(Fence(m.Request, 1500));
                    if (!string.IsNullOrEmpty(m.Response)) b.Append(Fence(m.Response, 3000));
                    b.Append('\n');
                    break;
                case "audio":
                    b.Append("📎 **").Append(m.Text).Append("** · transcript\n\n").Append(Quote(m.Response ?? string.Empty)).Append("\n\n---\n\n");
                    break;
                default:
                    b.Append("⚠️ **").Append(m.Text).Append("**\n\n---\n\n");
                    break;
            }
        }

        return b.ToString().TrimEnd('-', '\n', ' ');
    }
}
