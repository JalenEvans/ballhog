using System.Net;

namespace Ballhog.Http;

/// <summary>
/// The exception thrown when an NBA HTTP request fails in a way Ballhog cannot
/// transparently recover from, such as a blocked or otherwise non-retryable response.
/// </summary>
public sealed class BallhogHttpException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogHttpException"/> class.
    /// </summary>
    public BallhogHttpException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogHttpException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public BallhogHttpException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogHttpException"/> class
    /// with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public BallhogHttpException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogHttpException"/> class
    /// with a specified error message and the failing HTTP status code.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="statusCode">
    /// The HTTP status code returned by the failing response, or <see langword="null"/>
    /// when the failure did not originate from a response.
    /// </param>
    public BallhogHttpException(string? message, HttpStatusCode? statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BallhogHttpException"/> class
    /// with a specified error message, the failing HTTP status code, and a reference
    /// to the inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="statusCode">
    /// The HTTP status code returned by the failing response, or <see langword="null"/>
    /// when the failure did not originate from a response.
    /// </param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public BallhogHttpException(string? message, HttpStatusCode? statusCode, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the HTTP status code that caused the failure when the failure originated
    /// from a response; otherwise <see langword="null"/>.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }
}
