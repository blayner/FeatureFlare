namespace FeatureFlare.Application.Common;

public sealed class NotFoundException(string resource, string key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;

    public string Key { get; } = key;
}

/// <summary>The request conflicts with the current state (duplicate key, resource in use, ...).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Optimistic concurrency failure: the resource was modified by someone else.</summary>
public sealed class ConcurrencyException(string resource, string key, int? expected = null, int? actual = null)
    : Exception(expected is null
        ? $"{resource} '{key}' was modified concurrently. Reload and try again."
        : $"{resource} '{key}' is at version {actual}, but version {expected} was expected.");
