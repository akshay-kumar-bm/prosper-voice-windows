// Checks that the System.Text.Json port of Prosper's Json helper reads and writes the same shapes the .NET Framework
// JavaScriptSerializer did, on the payloads Prosper actually handles.
using System;
using System.Collections.Generic;
using Prosper;

static class Program
{
    static int failed, passed;

    static void Check(bool ok, string what)
    {
        if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + what); }
    }

    static int Main()
    {
        // A Responses API "response.completed" event with an MCP call, an approval request and a message.
        string ev = """
        {"type":"response.completed","sequence_number":42,"response":{"id":"resp_123","output":[
          {"type":"mcp_list_tools","server_label":"mcp","tools":[]},
          {"type":"mcp_call","id":"mcp_1","name":"list_secrets","arguments":"{\"folder\":\"/\"}","output":"[\"a\"]","error":null},
          {"type":"mcp_approval_request","id":"apr_1","name":"api_request","arguments":"{\"method\":\"post\",\"url\":\"https://api.github.com/repos/x/y/issues\"}"},
          {"type":"message","content":[{"type":"output_text","text":"Hello "},{"type":"output_text","text":"world"}]}
        ],"usage":{"input_tokens":12,"output_tokens":3.5}}}
        """;
        var o = Json.Obj(ev);
        Check(o != null, "event parses to an object");
        Check(Json.S_(o, "type") == "response.completed", "string value");
        Check(Json.S_(o, "sequence_number") == "42", "integer as text");
        var resp = Json.D(o, "response");
        Check(Json.S_(resp, "id") == "resp_123", "nested object");
        var output = Json.A(resp, "output");
        Check(output.Length == 4, "array length");
        Check(Json.S_(output[1], "error") == "" && !Json.Has(output[1], "error"), "null reads as missing");
        Check(Json.Has(output[1], "output"), "present value");
        Check(Json.S_(Json.D(resp, "usage"), "output_tokens") == "3.5", "double as invariant text");
        var parts = Json.A(output[3], "content");
        Check(parts.Length == 2 && Json.S_(parts[1], "text") == "world", "array of objects");
        Check(Json.A(o, "missing").Length == 0, "missing array is empty");
        Check(Json.Obj("not json") == null && Json.Obj("") == null && Json.Obj("[1,2]") == null, "bad input gives null");
        Check(Ai.Describe("api_request", Json.S_(output[2], "arguments")) == "POST api.github.com/repos/x/y/issues", "Describe() of a URL tool");
        Check(Ai.Describe("list_secrets", "{}") == "list secrets", "Describe() of a plain tool");

        // Request bodies: nested dictionaries and object arrays, as Ai.RunAssistant builds them.
        var body = Json.O("model", "gpt-6-luna", "stream", true, "previous_response_id", null,
            "input", new object[] { Json.O("role", "user", "content", new object[] { Json.O("type", "input_text", "text", "Hi \"there\" ✓") }) },
            "tools", new object[] { Json.O("type", "mcp", "require_approval", Json.O("never", Json.O("tool_names", new[] { "a", "b" }))) });
        string w = Json.Write(body);
        var back = Json.Obj(w);
        Check(!w.Contains("previous_response_id"), "null values are left out by Json.O");
        Check(Json.S_(back, "stream") == "true", "bool round-trips");
        Check(Json.S_(Json.A(Json.A(back, "input")[0], "content")[0], "text") == "Hi \"there\" ✓", "text with quotes and unicode round-trips");
        Check(Json.A(Json.D(Json.D(Json.A(back, "tools")[0], "require_approval"), "never"), "tool_names").Length == 2, "string[] serializes as an array");

        // Settings written by the original app (JavaScriptSerializer, PascalCase) load; missing new fields keep defaults.
        var s = Json.Read<Settings>("""{"ApiBase":"https://example.openai.azure.com/openai/v1","Model":"gpt-5-mini","TranscribeModel":"whisper-1","AboutMe":"Akash","Cleanup":false,"ScreenshotButton":true,"KeepHistory":true,"ShowBubble":true,"BarX":10,"BarY":20,"ApiKeySealed":"","McpOn":false,"McpServer":"","McpAllowed":"a, b","McpExpires":1700000000000}""");
        Check(s.ApiBase.EndsWith("/openai/v1") && s.Model == "gpt-5-mini" && !s.Cleanup && s.BarX == 10 && s.McpExpires == 1700000000000, "old settings file loads");
        Check(s.GlobalShortcuts, "new setting defaults on when absent");
        Check(s.Allowed().Count == 2 && s.Allowed()[1] == "b", "allowed tools parse");
        var fresh = Json.Read<Settings>(Json.Write(new Settings()));
        Check(fresh.Model == "gpt-6-luna" && !fresh.ShowBubble && fresh.GlobalShortcuts && fresh.BarX == -1, "settings round-trip");

        // Conversations (history files) round-trip.
        var c = new Conversation { Id = "1", Kind = "assistant", Title = "t", CreatedAt = 5, Messages = new List<Msg> {
            new Msg { Role = "user", Text = "write a reply" }, new Msg { Role = "tool", Text = "GET x", Ok = true, Ms = 120 },
            new Msg { Role = "assistant", Text = "**Hi**", Status = false } } };
        var c2 = Json.Read<Conversation>(Json.Write(c));
        Check(c2.Messages.Count == 3 && c2.Messages[1].Ms == 120 && c2.Messages[1].Ok && c2.Messages[2].Text == "**Hi**", "conversation round-trip");
        Check(History.Transcript(c2).Contains("User: write a reply") && History.Transcript(c2).Contains("(tool ran: GET x)"), "transcript for follow-ups");

        // The Assistant's message layout is unchanged.
        string msg = Ai.AssistMessage("make it formal", new AssistContext { App = "Outlook", Selected = "hey", AboutMe = "" });
        Check(msg.StartsWith("<app>\nOutlook\n</app>") && msg.Contains("<selected_text>\nhey\n</selected_text>") && !msg.Contains("about_me") && msg.EndsWith("<request>\nmake it formal\n</request>"), "assistant message");

        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
