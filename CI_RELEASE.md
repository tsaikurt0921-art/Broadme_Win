# CI / Release Guide

## 自動建置（PR / Push）
- Workflow: `.github/workflows/build-win.yml`
- 觸發：
  - push 到 `main` / `master`
  - PR 到 `main` / `master`
  - 手動 `workflow_dispatch`
- 產出：
  - Artifact `Broadme.Win-win-x64`

## 發佈（Tag）
- Workflow: `.github/workflows/release-win.yml`
- 觸發：
  - push tag `v*`（例如 `v1.1.0-beta.4`）
  - 或手動 `workflow_dispatch`
- 產出：
  - GitHub Release
  - 附件 `Broadme.Win-win-x64.zip`

## 建議流程
1. 合併到 `main`
2. 確認 `Build Broadme Win` workflow 綠燈
3. 建立 tag：
   - `git tag v1.1.0-beta.4`
   - `git push origin v1.1.0-beta.4`
4. 等 `Release Broadme Win` 產生 release 附件
