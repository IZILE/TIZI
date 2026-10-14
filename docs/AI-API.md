接口协议表示请求和响应的格式。设置页会按所选厂商列出已实现的格式，并在切换格式时同步默认接口地址；手动填写的网关地址会保留。每家厂商的密钥与设置草稿分别保存，密钥存入本机加密配置。已保存的模型名称不会被新版本的默认值覆盖。

当前实现包括 Responses API、兼容的 Chat Completions、Anthropic Messages 和 Gemini GenerateContent。自定义网关可选择全部四种格式。DeepSeek 提供 Chat、Responses 与 Messages 三种选择，具体支持范围以[官方文档](https://api-docs.deepseek.com/)、[Responses 说明](https://api-docs.deepseek.com/guides/responses_api/)及 [Anthropic 兼容说明](https://api-docs.deepseek.com/guides/anthropic_api/)为依据。

不同厂商采用相同格式，不代表具备相同的搜索工具。OpenAI 官方 Responses 接口使用原有搜索流程；其他已适配接口使用独立收集的公开网页证据，再交给模型整理。DeepSeek Responses 不请求其忽略的内置 web_search 工具，返回结果仍需通过实际网页证据校验。普通节点连接不会触发机场搜索；网站与 AI 检索由对应功能的明确操作触发。

兼容性检查使用隔离的模拟 HTTP 响应，覆盖请求地址、认证头、四类响应解析及证据校验，没有读取个人 API 密钥，也没有调用真实收费接口。第三方模型、配额与能力会变化；如果已有自定义网关，请按照该网关的文档填写模型和完整接口地址。
