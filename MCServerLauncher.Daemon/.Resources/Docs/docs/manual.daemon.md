# Daemon开发手册

## 前言

巴拉巴拉

## 功能

### 网页版登录

1. 使用http协议连接到/login?usr=<username>&pwd=<password>
   ，若登录成功，返回一个text，包含jwt。可以指定jwt的过期时间/login?usr=<username>&pwd=<password>&expired=<seconds>
   ，若expired缺省，默认30s。
2. 使用获取的jwt来登录到ws (/api/v1/token=<jwt>)，验证失败则会直接关闭ws连接

### WebSocket-Api

请检查ws-api.md

## 设计细节

### 远程模块

#### 验证模块

1. 用户信息(用户名，密码哈希，权限组，权限列表)存储至user.db
2. 密码加密采用Pbkdf2，salt大小16，key大小32，迭代10000次，算法采用SHA-256