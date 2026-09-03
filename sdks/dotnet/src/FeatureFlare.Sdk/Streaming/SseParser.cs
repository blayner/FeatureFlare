using System.Text;

namespace FeatureFlare.Sdk.Streaming;

internal sealed record SseMessage(string Event, string Data, string? Id);

/// <summary>Line-oriented parser for <c>text/event-stream</c> (WHATWG HTML §9.2).</summary>
internal sealed class SseParser
{
    private readonly StringBuilder _data = new();
    private string _event = string.Empty;
    private string? _id;
    private bool _hasData;

    public int? RetryMilliseconds { get; private set; }

    public string? LastEventId { get; private set; }

    /// <summary>Feeds one line (without its terminator). Returns a message when a blank line completes one.</summary>
    public SseMessage? ProcessLine(string line)
    {
        if (line.Length == 0)
        {
            return Dispatch();
        }

        if (line[0] == ':')
        {
            return null; // comment / heartbeat
        }

        var colon = line.IndexOf(':');
        var field = colon < 0 ? line : line[..colon];
        var value = colon < 0 ? string.Empty : line[(colon + 1)..];
        if (value.StartsWith(' '))
        {
            value = value[1..];
        }

        switch (field)
        {
            case "event":
                _event = value;
                break;
            case "data":
                if (_hasData)
                {
                    _data.Append('\n');
                }

                _data.Append(value);
                _hasData = true;
                break;
            case "id" when !value.Contains('\0'):
                _id = value;
                break;
            case "retry" when int.TryParse(value, out var retry) && retry >= 0:
                RetryMilliseconds = retry;
                break;
        }

        return null;
    }

    private SseMessage? Dispatch()
    {
        if (_id is not null)
        {
            LastEventId = _id;
        }

        SseMessage? message = _hasData
            ? new SseMessage(_event.Length == 0 ? "message" : _event, _data.ToString(), _id)
            : null;

        _event = string.Empty;
        _id = null;
        _data.Clear();
        _hasData = false;
        return message;
    }
}
