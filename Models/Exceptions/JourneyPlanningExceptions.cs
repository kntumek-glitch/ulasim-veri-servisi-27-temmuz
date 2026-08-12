using System;

namespace TransportDataService.Models.Exceptions;

public class SnapshotUnavailableException : Exception
{
    public SnapshotUnavailableException(string message) : base(message) { }
}

public class NoNearbyStopException : Exception
{
    public bool IsOrigin { get; }
    
    public NoNearbyStopException(string message, bool isOrigin) : base(message)
    {
        IsOrigin = isOrigin;
    }
}

public class NoActiveServiceException : Exception
{
    public NoActiveServiceException(string message) : base(message) { }
}

public class FeedStaleException : Exception
{
    public FeedStaleException(string message) : base(message) { }
}
