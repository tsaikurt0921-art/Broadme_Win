# Broadme_Win

這是 Broadme 的 Windows 10/11 重製專案（WPF + .NET 8）。

## 已完成

1. 啟動流程（對齊 mac 版）
- Launch 視窗（1.5 秒）
- 啟動時檢查本地序號
- 有序號時向 API 重新驗證
- 無序號或失效時進入序號綁定視窗

2. 序號驗證（正式 API 串接）
- API: `POST /beta/keys/validate/`
- 預設 Base URL: `http://57.181.68.103`
- 欄位：`key`, `client_uid`

3. 視覺還原（第二輪）
- 主視窗改為 WindowChrome 圓角無標題列
- Main/Launch/Serial/QRCode 視窗統一圓角風格
- 導入原版 `app-logo` 與 `close-btn` 資產
- 控制授權視窗 450x650：六格 PIN + QR + 控制網址

4. 串流與控制
- `GET /`, `/stream`, `/stream-control`, `/control`
- `POST /api/auth/pin`, `/api/input`, `/api/control/check`, `/api/control/revoke`, `/api/upload-photo`
- 遠端輸入控制（move/click/doubleClick/scroll）
- 標註 overlay（annotationStart/move/end/clearAnnotations）

## 下一步建議

1. 根據你提供的 mac 截圖做第三輪 px 級校準（逐元件 x/y/width/height）
2. 視窗開關動畫與 hover/pressed 交互細節一致化
3. 進行實機編譯驗證（需安裝 dotnet SDK）

## 注意

目前此環境未安裝 `dotnet`，尚未在此機器完成編譯驗證。
