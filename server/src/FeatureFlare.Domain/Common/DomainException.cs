namespace FeatureFlare.Domain.Common;

/// <summary>Raised when an operation would violate an aggregate invariant.</summary>
public sealed class DomainException(string message) : Exception(message);
