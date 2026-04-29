namespace Broadme.Win.Models;

public sealed class ValidateSerialRequest
{
    public string key { get; set; } = string.Empty;
    public string client_uid { get; set; } = string.Empty;
}

public sealed class ValidateSerialResponse
{
    public bool validation { get; set; }
    public string message { get; set; } = string.Empty;
}
