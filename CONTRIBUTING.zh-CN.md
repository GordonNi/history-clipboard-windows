# 参与 HistoryClipboard Windows 版开发

简体中文 | [English](CONTRIBUTING.md)

感谢你帮助改进 HistoryClipboard。参与本项目即表示同意遵守
[行为准则](CODE_OF_CONDUCT.zh-CN.md)。

## 提交 Issue 前

- 先搜索已有 Issue。
- 尽量使用缺陷或功能建议表单。
- 安全漏洞请遵循 [SECURITY.zh-CN.md](SECURITY.zh-CN.md)，不要公开提交。
- 不要公开真实剪贴板内容、真实 `history.db`、`settings.json`、缓存图片、凭据、
  个人路径或含敏感信息的截图。

## 开发环境

本仓库面向 Windows 10/11 x64、.NET 10 和 WPF。安装 `global.json` 指定的 SDK 后运行：

```powershell
dotnet restore HistoryClipboard.slnx
dotnet format HistoryClipboard.slnx --verify-no-changes
dotnet build HistoryClipboard.slnx --configuration Release --no-restore
dotnet test HistoryClipboard.slnx --configuration Release --no-build
dotnet list HistoryClipboard.slnx package --vulnerable --include-transitive
```

修改存储、Windows 系统集成、打包、隐私行为或未来同步设计前，请阅读 `docs/`
中的相关文档。

## Pull Request

- 每个 PR 保持单一目标，并便于审阅。
- 行为变化必须新增或更新测试。
- 保持默认仅在本机工作；不要加入遥测、内容日志、广告或未加密剪贴板传输。
- 将 Windows 集成放在清晰边界后，使核心行为可以测试。
- 用户可见行为变化应同步更新公开文档。
- 运行上述命令，并说明完成的人工验证。
- 不要提交构建输出、发布产物、本地应用数据或私有工具二进制。

维护者可能在合并前要求修改。贡献内容按本仓库的
[MIT License](LICENSE) 授权。
