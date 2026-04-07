# 热词表建议

这份清单是根据你刚才的语音测试结果整理的，目标是优先修正“高频、容易错、对项目场景重要”的词。

## 第一批建议优先加入

```text
gxVoiceIn
Volcengine
ASR
WebSocket
WPF
HUD
剪贴板
GitHub
OpenAI
Prompt
API
Token
Shift
Ctrl+V
Caps Lock
热词表
上下文
状态机
异步
注入
托盘
自启动
火山引擎
实时转写
语音输入法
```

## 这次测试里出现的典型误识别

- `GX was in`，目标词更像 `gxVoiceIn`
- `Webshop`，目标词更像 `WebSocket`
- `get up`，目标词更像 `GitHub`
- `Openai`，目标词是 `OpenAI`
- `Probat`，目标词更像 `Prompt`
- `Types lock`，目标词更像 `Caps Lock`
- `剪切板`，建议统一成 `剪贴板`

## 使用建议

- 优先加入你平时会反复说的产品名、技术名词、英文术语。
- 如果一个词你自己都不太会读，热词表帮助有限，最好同时调整说法。
- 比如 `Volcengine` 不顺口时，直接说 `火山引擎` 往往更稳。
- `Ctrl+V`、`Caps Lock` 这种词，尽量每次都用同一种说法。

## 下一步建议

- 先把“第一批建议优先加入”录进火山热词表。
- 录完后再用同一段测试文本复测一次。
- 把新的识别结果和原文再对比，就能继续筛第二批词。
