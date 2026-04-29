namespace Broadme.Win.Models;

public sealed class ApiSettings
{
    public string BaseUrl { get; set; } = "http://57.181.68.103";
}

public sealed class ValidateSerialApiRequest
{
    public string key { get; set; } = string.Empty;
    public string client_uid { get; set; } = string.Empty;
}

public sealed class ValidateSerialApiResponse
{
    public bool validation { get; set; }
    public string message { get; set; } = string.Empty;
}
