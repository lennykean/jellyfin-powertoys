using System;

namespace JellyfinPowertoys.Jellywrap;

internal sealed class VideoEncodingException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
