# 公开版核查 / Public-edition review / Prüfung der öffentlichen Version

- 默认人员、医院与就诊地点统一使用通用占位文字；复诊模板不携带真实出诊排班。
- 模板键、按钮绑定、参考原文、测试输入和生成正文保持一致；两份模板仍独立保存和切换。
- 应用及安装器从公开版源码重新构建，环境包只包含新程序和微软运行组件。
- 公开版草稿目录和安装器标识与原私人部署分开，发布操作不改动用户原本地程序。
- 旧文书仓库保留为私有备份；新公开仓库从脱敏文件建立干净历史，旧安装包不对外发布。
- The public source, default resources, rebuilt binaries and nested installer payload are checked before release. No private-edition installer is bundled.
- Die öffentlichen Quellen, Standardressourcen, neu erstellten Programmdateien und eingebetteten Installationsdateien werden vor der Veröffentlichung geprüft.
