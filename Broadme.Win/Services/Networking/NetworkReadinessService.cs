namespace Broadme.Win.Services.Networking;

public static class NetworkReadinessService
{
    public static string BuildFirstRunHint(int port)
    {
        return
            "首次廣播提醒:\n" +
            $"1. 當 Windows 彈出「安全性警訊」時，請務必點擊「允許存取」\n" +
            "2. 請確認網路設定為「專用」(Private) 而非「公用」(Public)\n" +
            "3. 請確認觀看裝置與主機在同一個區域網路 (Wi-Fi)\n" +
            "4. 若仍無法連線，請暫時關閉防毒軟體的防火牆功能再試";
    }
}
