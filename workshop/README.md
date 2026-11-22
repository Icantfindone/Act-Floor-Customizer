# Steam Workshop 发布文件

这个目录保存工坊条目的**元数据与封面**，是以后更新的唯一依据。

- `workshop.json` — 标题、描述、标签、依赖、可见性。标签在网页上无法修改，只能在这里定。
- `image.png` — 封面图，必须小于 1 MB。
- `mod_id.txt` — 工坊条目 ID `3843992717`。**不要删**，更新同一个条目靠它。
- 工坊页面：https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717

上传器（Mega Crit 官方）：https://github.com/megacrit/sts2-mod-uploader

## 更新流程

1. 准备好新的构建产物：`package/ActFloorCustomizer/` 里的 `ActFloorCustomizer.dll` 和 `ActFloorCustomizer.json`。
2. 在**上传器工作区**（本机是 `..\ActFloorCustomizer-Workshop\ActFloorCustomizer\`，内含 `ModUploader.exe` 的那一层）里：

   ```text
   content\ActFloorCustomizer\    ← 用上一步的两个文件覆盖
   workshop.json                  ← 用本目录的副本覆盖，并把 changeNote 写成这次更新的说明
   image.png                      ← 如需换封面，同样小于 1 MB
   mod_id.txt                     ← 保持 3843992717 不变
   ```

3. 关掉游戏，确认 Steam 已登录，双击工作区上一层的 `upload.cmd`（等价于 `ModUploader.exe upload -w ActFloorCustomizer`）。
4. 看到 `Successfully uploaded ... with id 3843992717` 即为更新成功；失败看 `mod-uploader.log`。

## 注意

- `ModUploader.exe`、`steam_api64.dll`、`steam_appid.txt` 是第三方二进制，**不放进仓库**，用的时候从官方 release 下载：`ModUploader-win-x64.zip`。
- 工坊 `content` 只需要模组文件（`dll` + `json`），不要放安装脚本、说明文档或 ZIP。
- 首次上传后条目默认可见性是 `private`，确认无误再在工坊页面改成 `public`。
