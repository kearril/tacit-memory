# TacitMemory 检索与性能基准测试

仓库内置了独立的基准测试控制台工具 `benchmarks/TacitMemory.Benchmarks` 与脱敏语料 `benchmarks/corpus/`，用于量化评测本地检索索引性能与不同辅助模型组合的召回质量。该工具仅面向开发调优与模型选型，不包含在预编译发行包内，也不随 `dotnet test` 自动执行。

## 1. 两种评测模式

- **`recall`（召回与精排质量）**：在隔离临时目录中复刻语料库，严格经由 `TacitMemory.Core` 公开门面 `TraceRecallAsync` 跑完与 Agent 一致的完整检索管线，输出 `Recall@5`、`Hit@1`、`All@5`、`MRR`、负例误召回与拒答率、四阶段漏斗（`FullText` → `Vector` → `Fused` → `Shown`）以及未命中归因（`not_recalled` / `beyond_candidates` / `below_threshold` / `beyond_top_k` / `unreadable`）。
  - 内置回环缓存代理：将模型响应按 `(model, text)` / `(model, query, passage)` 缓存于本地 `benchmarks/.cache/model-cache.db`（已忽略提交），离线复跑与调参时只转发未命中条目，支持 `--no-cache` 强制直连。
- **`perf`（索引与本地检索性能）**：按指定规模（默认 `500` / `2000` / `10000` 检索单元，`1024` 维向量）确定性生成合成语料，使用零延迟回环模型替身，分别测量冷建同步（`SyncCold`）、无变更增量同步（`SyncInc`）、`FTS5` 全文检索、本地向量检索与端到端管线的 P50/P95 延迟及托管堆分配。

## 2. 常用命令

```bash
# 运行本地索引与检索性能压测（建议 Release 构建）
dotnet run -c Release --project benchmarks/TacitMemory.Benchmarks -- perf

# 复用本机 config.yaml 中的嵌入与重排模型配置（仅只读提取，不触碰真实记忆库）
dotnet run -c Release --project benchmarks/TacitMemory.Benchmarks -- recall \
  --from-config ~/.tacit/config.yaml --strict-corpus

# 临时替换为其他模型端点进行横评，并保存结果或与基线对比
dotnet run -c Release --project benchmarks/TacitMemory.Benchmarks -- recall \
  --from-config ~/.tacit/config.yaml \
  --reranker-url http://127.0.0.1:8082/v1/rerank --reranker-model my-reranker \
  --min-rerank-score 0.30 \
  --baseline benchmarks/results/baseline.json \
  --save benchmarks/results/candidate.json

# 使用自定义私有语料库评测（目录结构需含 memory/ 与 queries.yaml）
dotnet run -c Release --project benchmarks/TacitMemory.Benchmarks -- recall \
  --from-config ~/.tacit/config.yaml --corpus /path/to/my-corpus
```

完整命令行参数以 `dotnet run --project benchmarks/TacitMemory.Benchmarks -- --help` 为准。

## 3. 内置语料设计

`benchmarks/corpus/` 为完全脱敏的中文为主、混合英文技术术语与代码标识的语料库（38 篇细节页、跨 4 个月的 47 条活动与 11 条周/月汇总、78 条查询，通过 `--strict-corpus` 零格式告警校验），覆盖 9 类检索场景：

| 类别 | 数量 | 评测目标 |
| :--- | ---: | :--- |
| `exact-code` | 8 | 错误码、配置键、CLI 参数与代码标识的精确词面召回 |
| `paraphrase-zh` | 9 | 口语化中文改写与弱词面重叠下的语义召回 |
| `bilingual` | 8 | 纯英文与中英混排提问对中文记忆页的跨语言召回 |
| `cross-field` | 5 | 标题与小节正文跨字段组合匹配 |
| `filtered` | 5 | `workstream`、`from`/`to`、`kind` 联合筛选与周汇总命中 |
| `distractor` | 10 | 同主线高混淆近邻页面间的精准排序区分 |
| `temporal` | 10 | 跨月演进、已推翻决策（`Overturned`）与现行方案区分、历史活动/月汇总定位 |
| `multi-hop` | 6 | 需同时召回两篇关联细节页的复合问题（检验 `All@5`） |
| `negative` | 17 | 仓库未记录的主题与同技术栈近邻陷阱（检验误召回与拒答边界） |

## 4. 参考基线（`v1.0.1`）

### 本地索引与检索性能

环境：Apple Silicon（Release 构建、`dim=1024`、`warmup=5`、`iterations=30`、不含外部模型推理耗时）。

| 检索单元数 | 冷建同步 (`SyncCold`) | 增量同步 (`SyncInc`) | FTS5 P50 / P95 | FTS5 分配 | Vector P50 / P95 | Vector 分配 | End-to-End P50 / P95 | End-to-End 分配 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 500 | 53.6 ms | 2.1 ms | 0.17 / 0.22 ms | 24.3 KB | 0.50 / 0.95 ms | 33.1 KB | 1.42 / 2.33 ms | 327.2 KB |
| 2,000 | 95.6 ms | 5.4 ms | 0.23 / 0.43 ms | 23.0 KB | 1.60 / 1.61 ms | 33.2 KB | 2.64 / 2.80 ms | 364.8 KB |
| 10,000 | 544.5 ms | 34.6 ms | 0.74 / 1.94 ms | 23.1 KB | 7.66 / 8.00 ms | 33.4 KB | 9.95 / 12.01 ms | 364.6 KB |

向量检索采用零拷贝流式扫描、SIMD 余弦计算与有界 Top-K 堆回表，单次查询内存分配随语料规模保持恒定（~33 KB）。

### 召回参考（本地 `Qwen3-0.6B` 组合）

内置 78 条查询（61 正例 + 17 负例），使用本地 `Qwen3-Embedding-0.6B` + `Qwen3-Reranker-0.6B`：

| `minRerankScore` | `Recall@5` | `Hit@1` | `All@5` | `MRR` | 负例拒答率 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| `0.001`（默认） | 100.0% | 95.1% | 100.0% | 0.975 | 0.0% (0/17) |
| `0.30` | 100.0% | 95.1% | 100.0% | 0.975 | 64.7% (11/17) |
| `0.50` | 100.0% | 95.1% | 100.0% | 0.975 | 64.7% (11/17) |

不同重排模型的分数分布差异较大；如需压低无关结果的噪声，可结合实际使用的重排模型用 `recall --min-rerank-score <值>` 实测后在 `config.yaml` 中调整 `retrieval.minRerankScore`。
