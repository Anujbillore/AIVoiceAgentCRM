using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/voice")]
public class VoiceController : ControllerBase
{
    private readonly IVoiceAgentService _voice;
    private readonly ISarvamAiService _ai;
    private readonly IVoiceSessionStore _sessions;
    private readonly IExotelMediaService _exotel;
    private readonly IExotelPhoneService _phones;
    private readonly IConfiguration _config;

    public VoiceController(IVoiceAgentService voice, ISarvamAiService ai, IVoiceSessionStore sessions, IExotelMediaService exotel, IExotelPhoneService phones, IConfiguration config)
    {
        _voice = voice;
        _ai = ai;
        _sessions = sessions;
        _exotel = exotel;
        _phones = phones;
        _config = config;
    }

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<VoiceStatusDto>> Status(CancellationToken cancellationToken)
    {
        return await _voice.GetStatusAsync(PublicRoot(), cancellationToken);
    }

    [HttpPost("session")]
    [Authorize]
    public async Task<ActionResult<VoiceSessionDto>> Start(VoiceSessionStartRequest request, CancellationToken cancellationToken)
    {
        return await _voice.StartAsync(request, null, cancellationToken);
    }

    [HttpPost("session/{id}/turn")]
    [Authorize]
    public async Task<ActionResult<VoiceSessionDto>> Turn(string id, VoiceSessionTurnRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _voice.TurnAsync(id, request.SpokenText ?? string.Empty, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("session/{id}/audio")]
    [Authorize]
    public async Task<ActionResult<VoiceSessionDto>> TurnAudio(string id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Audio file is required." });
        }

        string transcript;
        if (_ai.IsConfigured)
        {
            await using var stream = file.OpenReadStream();
            transcript = await _ai.TranscribeAsync(stream, file.FileName, file.ContentType ?? "audio/webm", cancellationToken);
        }
        else
        {
            return StatusCode(501, new { message = "Add Sarvam:ApiKey for live speech-to-text, or type what the caller said." });
        }

        try
        {
            return await _voice.TurnAsync(id, transcript, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("session/{id}/end")]
    [Authorize]
    public async Task<ActionResult<VoiceSessionDto>> End(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await _voice.EndAsync(id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("simulate")]
    [Authorize]
    public async Task<ActionResult<VoiceTurnResponse>> Simulate(SimulateCallRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _voice.HandleIncomingAsync(request, cancellationToken));
    }

    [HttpPost("incoming")]
    [AllowAnonymous]
    public async Task<ActionResult<VoiceTurnResponse>> Incoming(SimulateCallRequest request, CancellationToken cancellationToken)
    {
        return await _voice.HandleIncomingAsync(request, cancellationToken);
    }

    [HttpPost("twilio/incoming")]
    [AllowAnonymous]
    public async Task<IActionResult> TwilioIncoming([FromForm] string? From, [FromForm] string? CallerName, [FromForm] string? CallSid, CancellationToken cancellationToken)
    {
        var started = await _voice.StartAsync(
            new VoiceSessionStartRequest(CallerName ?? "Unknown", From ?? ""),
            CallSid,
            cancellationToken);
        var gatherUrl = $"{Request.Scheme}://{Request.Host}/api/voice/twilio/gather";
        return Content(TwimlGather(started.ReplyText, gatherUrl), "application/xml");
    }

    [HttpPost("twilio/gather")]
    [AllowAnonymous]
    public async Task<IActionResult> TwilioGather(
        [FromForm] string? SpeechResult,
        [FromForm] string? From,
        [FromForm] string? CallSid,
        CancellationToken cancellationToken)
    {
        var session = CallSid is null ? null : _sessions.GetByExternal(CallSid);
        VoiceSessionDto result;
        if (session is null)
        {
            var started = await _voice.StartAsync(new VoiceSessionStartRequest("Unknown", From ?? ""), CallSid, cancellationToken);
            result = await _voice.TurnAsync(started.SessionId, SpeechResult ?? "", cancellationToken);
        }
        else
        {
            result = await _voice.TurnAsync(session.Id, SpeechResult ?? "", cancellationToken);
        }

        if ((result.TransferType is "Warm" or "Cold") && !string.IsNullOrWhiteSpace(result.TransferNumber))
        {
            var dial = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                  <Say>{System.Security.SecurityElement.Escape(result.ReplyText)}</Say>
                  <Dial>{System.Security.SecurityElement.Escape(result.TransferNumber)}</Dial>
                </Response>
                """;
            return Content(dial, "application/xml");
        }

        if (result.Ended)
        {
            var hangup = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <Response>
                  <Say>{System.Security.SecurityElement.Escape(result.ReplyText)}</Say>
                  <Hangup/>
                </Response>
                """;
            return Content(hangup, "application/xml");
        }

        var gatherUrl = $"{Request.Scheme}://{Request.Host}/api/voice/twilio/gather";
        return Content(TwimlGather(result.ReplyText, gatherUrl), "application/xml");
    }

    [HttpGet("exotel/numbers")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<IReadOnlyList<ExotelNumberDto>>> ExotelNumbers(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _phones.ListNumbersAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("exotel/attach")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ExotelAttachResult>> AttachExotel(ExotelAttachRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneSid))
        {
            return BadRequest(new { message = "PhoneSid is required." });
        }

        try
        {
            return await _phones.AttachIncomingAsync(request.PhoneSid, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("exotel/incoming")]
    [HttpPost("exotel/incoming")]
    [AllowAnonymous]
    public async Task<IActionResult> ExotelIncoming(CancellationToken cancellationToken)
    {
        var callSid = ReadParam("CallSid");
        var from = ReadParam("CallFrom", "From") ?? "";
        var callerName = ReadParam("CallerName") ?? "Unknown";

        var existing = callSid is null ? null : _sessions.GetByExternal(callSid);
        if (existing is { Ended: false })
        {
            var last = existing.Turns.LastOrDefault(t => t.Role == "agent")?.Text ?? "Hello, thank you for calling.";
            return Content(ExoMl.PromptAndRecord(last, TurnUrl(), PlayUrl(existing.Id)), "application/xml");
        }

        var started = await _voice.StartAsync(new VoiceSessionStartRequest(callerName, from), callSid, cancellationToken);
        return Content(ExoMlFor(started), "application/xml");
    }

    [HttpGet("exotel/turn")]
    [HttpPost("exotel/turn")]
    [AllowAnonymous]
    public async Task<IActionResult> ExotelTurn(CancellationToken cancellationToken)
    {
        var callSid = ReadParam("CallSid");
        var from = ReadParam("CallFrom", "From") ?? "";
        var recordingUrl = ReadParam("RecordingUrl", "recording_url", "RecordingURL");

        var session = callSid is null ? null : _sessions.GetByExternal(callSid);
        if (session is null)
        {
            var started = await _voice.StartAsync(new VoiceSessionStartRequest("Unknown", from), callSid, cancellationToken);
            session = _sessions.Get(started.SessionId);
        }

        if (session is null)
        {
            return Content(ExoMl.SayAndHangup("I am sorry, this call could not be started. Goodbye.", null), "application/xml");
        }

        if (string.IsNullOrWhiteSpace(recordingUrl))
        {
            return Content(ExoMl.PromptAndRecord("Sorry, I did not catch that. Please speak after the tone.", TurnUrl(), PlayUrl(session.Id)), "application/xml");
        }

        if (!_ai.IsConfigured)
        {
            var ended = await _voice.EndAsync(session.Id, cancellationToken);
            return Content(ExoMl.SayAndHangup("Speech recognition is not configured. Please add a Sarvam API key and call again. Goodbye.", PlayUrl(ended.SessionId)), "application/xml");
        }

        var audio = await _exotel.DownloadRecordingAsync(recordingUrl, cancellationToken);
        if (audio is null || audio.Length == 0)
        {
            return Content(ExoMl.PromptAndRecord("I could not hear that clip. Please say that again.", TurnUrl(), null), "application/xml");
        }

        var (fileName, contentType) = DetectRecordingFormat(audio, recordingUrl);
        string transcript;
        try
        {
            using var stream = new MemoryStream(audio);
            transcript = await _ai.TranscribeAsync(stream, fileName, contentType, cancellationToken);
        }
        catch (Exception)
        {
            return Content(ExoMl.PromptAndRecord("I could not understand that. Please say that again.", TurnUrl(), null), "application/xml");
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return Content(ExoMl.PromptAndRecord("Sorry, I did not catch that. Please speak after the tone.", TurnUrl(), null), "application/xml");
        }

        VoiceSessionDto result;
        try
        {
            result = await _voice.TurnAsync(session.Id, transcript, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            var restarted = await _voice.StartAsync(new VoiceSessionStartRequest("Unknown", from), callSid, cancellationToken);
            result = await _voice.TurnAsync(restarted.SessionId, transcript, cancellationToken);
        }

        return Content(ExoMlFor(result), "application/xml");
    }

    [HttpGet("exotel/audio/{sessionId}")]
    [AllowAnonymous]
    public IActionResult ExotelAudio(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session?.ReplyAudio is null || session.ReplyAudio.Length == 0)
        {
            return NotFound();
        }

        return File(session.ReplyAudio, DetectAudioType(session.ReplyAudio));
    }

    [HttpPost("transcribe")]
    [Authorize]
    public async Task<IActionResult> Transcribe(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Audio file is required." });
        }

        if (!_ai.IsConfigured)
        {
            return StatusCode(501, new { message = "Add Sarvam:ApiKey to use live speech-to-text, or type the caller’s words instead." });
        }

        await using var stream = file.OpenReadStream();
        var transcript = await _ai.TranscribeAsync(stream, file.FileName, file.ContentType ?? "audio/webm", cancellationToken);
        return Ok(new { transcript });
    }

    private string ExoMlFor(VoiceSessionDto result)
    {
        if ((result.TransferType is "Warm" or "Cold") && !string.IsNullOrWhiteSpace(result.TransferNumber))
        {
            return ExoMl.SayAndDial(result.ReplyText, result.TransferNumber, PlayUrl(result.SessionId));
        }

        return result.Ended
            ? ExoMl.SayAndHangup(result.ReplyText, PlayUrl(result.SessionId))
            : ExoMl.PromptAndRecord(result.ReplyText, TurnUrl(), PlayUrl(result.SessionId));
    }

    private string TurnUrl() => $"{PublicRoot()}/api/voice/exotel/turn";

    private string? PlayUrl(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        if (session?.ReplyAudio is null || session.ReplyAudio.Length == 0)
        {
            return null;
        }

        return $"{PublicRoot()}/api/voice/exotel/audio/{sessionId}?v={DateTime.UtcNow.Ticks}";
    }

    private string PublicRoot()
    {
        var configured = _config["Voice:PublicBaseUrl"];
        return string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}"
            : configured.TrimEnd('/');
    }

    private string? ReadParam(params string[] names)
    {
        foreach (var name in names)
        {
            if (Request.Query.TryGetValue(name, out var query) && !string.IsNullOrWhiteSpace(query))
            {
                return query.ToString();
            }

            if (Request.HasFormContentType && Request.Form.TryGetValue(name, out var form) && !string.IsNullOrWhiteSpace(form))
            {
                return form.ToString();
            }
        }

        return null;
    }

    private static (string FileName, string ContentType) DetectRecordingFormat(byte[] audio, string url)
    {
        if (audio.Length >= 4 && audio[0] == (byte)'R' && audio[1] == (byte)'I' && audio[2] == (byte)'F' && audio[3] == (byte)'F')
        {
            return ("caller.wav", "audio/wav");
        }

        if (url.Contains(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return ("caller.wav", "audio/wav");
        }

        return ("caller.mp3", "audio/mpeg");
    }

    private static string DetectAudioType(byte[] audio)
    {
        if (audio.Length >= 4 && audio[0] == (byte)'R' && audio[1] == (byte)'I' && audio[2] == (byte)'F' && audio[3] == (byte)'F')
        {
            return "audio/wav";
        }

        if (audio.Length >= 3 && audio[0] == (byte)'I' && audio[1] == (byte)'D' && audio[2] == (byte)'3')
        {
            return "audio/mpeg";
        }

        return "audio/wav";
    }

    private static string TwimlGather(string say, string gatherUrl) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Response>
          <Gather input="speech" action="{gatherUrl}" method="POST" timeout="6" speechTimeout="auto">
            <Say>{System.Security.SecurityElement.Escape(say)}</Say>
          </Gather>
          <Say>Sorry, I did not catch that. Goodbye.</Say>
        </Response>
        """;
}
