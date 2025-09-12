[//]: # (- [ ] 增加任务型操作)

[//]: # (    - [ ] 新增新的通道ActionProgress，依附于Notification通道)

[//]: # (    - [ ] 分解长任务为多个原子任务, 例如实例安装, 并在Context中添加ProgressMonitor)

[//]: # (    - [ ] 可能的重构Rpc系统, 添加方法的依赖注入&#40;ProgressMonitor&#41;)
- [ ] 文件操作
    - [ ] 删除Up/Download Chunk的Action, 更新为Http方法
    - [ ] 为http服务器添加文件上传下载功能
- [ ] RPC系统
    - [ ] 将信息载体从JSON改为MsgPack
    - [ ] 事件系统: 将event meta改为过滤器