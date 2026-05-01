namespace Broadme.Win.Services.Networking;

public static class NetworkReadinessService
{
    public static string BuildFirstRunHint(int port)
    {
        return
            "首次廣播提醒:\n" +
            $"1. 請確認 Windows 防火牆允許 Broadme 或 TCP {port}\n" +
            "2. 請確認網路設定為「專用」(Private) 而非「公用」(Public)\n" +
            "3. 請確認觀看裝置與主機在同一個區域網路 (Wi-Fi)\n" +
            "4. 若無法連線，請嘗試以「系統管理員身分」重新啟動程式";
    }
}
