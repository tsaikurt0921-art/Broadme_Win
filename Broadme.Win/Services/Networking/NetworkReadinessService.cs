namespace Broadme.Win.Services.Networking;

public static class NetworkReadinessService
{
    public static string BuildFirstRunHint(int port)
    {
        return
            "首次廣播提醒:\n" +
            $"1. 請確認 Windows 防火牆允許 Broadme 或 TCP {port}\n" +
            "2. 請確認觀看裝置與主機在同一個區域網路\n" +
            "3. 若無法連線，先關閉再重新開始廣播一次";
    }
}
