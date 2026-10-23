# 验证方法

在仓库根目录使用 PowerShell 7 执行。

## 解码回归检查

```powershell
pwsh -NoProfile -File tests/RunDecoderChecks.ps1
```

直接编译仓库中的 UniGif 源码，使用最小 Unity 类型替身检查透明叠加、disposal 1/2/3、连续恢复、交错行、边界裁剪、帧延迟、取消清理和首帧解析提前结束。无需安装游戏；PowerShell 7 需包含 Roslyn 程序集。

替身用于验证像素结果和释放调用，不能代替游戏中的 Unity 生命周期、GPU 开销或帧率测试。

## Release 编译验证

```powershell
pwsh -NoProfile -File tests/Compile.ps1
```

从 GifDisplay/GifDisplay.csproj 读取源码和依赖，用 PowerShell 自带 Roslyn 编译。需要安装游戏依赖及 .NET Framework 4.8.1 引用程序集；依赖路径沿用项目文件中的 HintPath。默认输出 artifacts/GifDisplay.dll。

脚本用于检查源码能否编译，不执行完整 MSBuild 流程；它会忽略不存在的引用路径，依赖解析警告仍应在 Rider/MSBuild 中检查。

复现特定构建的引用组合时，可以传入包含编译器 /reference 参数的日志：

```powershell
pwsh -NoProfile -File tests/Compile.ps1 -BuildLog C:/path/to/build.log
```

日志只在本地读取，不需要提交到仓库。生成的 DLL 已由 .gitignore 排除。
