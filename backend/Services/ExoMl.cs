using System.Security;

namespace AiVoicePortal.Api.Services;

public static class ExoMl
{
    public static string PromptAndRecord(string say, string recordAction, string? playUrl)
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
              {Prompt(say, playUrl)}
              <Record action="{SecurityElement.Escape(recordAction)}" method="POST" maxLength="20" finishOnKey="#" />
              <Say>Sorry, I did not catch that. Goodbye.</Say>
            </Response>
            """;
    }

    public static string SayAndHangup(string say, string? playUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
          {Prompt(say, playUrl)}
          <Hangup/>
        </Response>
        """;

    public static string SayAndDial(string say, string number, string? playUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
          {Prompt(say, playUrl)}
          <Dial>{SecurityElement.Escape(number)}</Dial>
        </Response>
        """;

    private static string Prompt(string say, string? playUrl) =>
        !string.IsNullOrWhiteSpace(playUrl)
            ? $"<Play>{SecurityElement.Escape(playUrl)}</Play>"
            : $"<Say>{SecurityElement.Escape(say)}</Say>";
}
