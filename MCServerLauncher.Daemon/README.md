# 常见的开发问题

------

### 曾经运行过Daemon, pull后运行发现报错
- 如果是Daemon启动时 (http服务运行前) Json解析报错, 说明某些配置文件类结构已经更改, 删除相应的json即可, 例如./config.json、./daemon/instances/<uuid>/daemon_instance.json等

### 待补充...