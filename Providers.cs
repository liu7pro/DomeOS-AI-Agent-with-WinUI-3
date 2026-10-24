using System.Collections.Generic;

namespace DomeOSAgentDev
{
    /// <summary>A preset: picking one fills in the protocol, the base URL and a sensible model.</summary>
    public sealed class ProviderPreset
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Proto { get; set; } = "openai";
        public string Base { get; set; } = "";
        public string Model { get; set; } = "";
        public string Keys { get; set; } = "";
        public string Note { get; set; } = "";
        public string Group { get; set; } = "";
    }

    /// <summary>The catalogue of AI services DomeOS AI knows how to talk to.</summary>
    public static class Providers
    {
        private static ProviderPreset P(string id, string name, string proto, string baseUrl, string model,
                                        string keys, string note, string group)
        {
            return new ProviderPreset
            {
                Id = id,
                Name = name,
                Proto = proto,
                Base = baseUrl,
                Model = model,
                Keys = keys,
                Note = note,
                Group = group,
            };
        }

        public static readonly List<ProviderPreset> All = new List<ProviderPreset>
        {
            // first party labs
            P("deepseek", "DeepSeek 深度求索", "openai", "https://api.deepseek.com/v1", "deepseek-chat", "platform.deepseek.com", "deepseek-chat 自动指向最新 V4.1-Flash", "labs"),
            P("openai", "OpenAI", "openai", "https://api.openai.com/v1", "gpt-6.1-sol", "platform.openai.com", "旗舰 gpt-6-astra；低价 gpt-6-luna", "labs"),
            P("anthropic", "Anthropic Claude", "anthropic", "https://api.anthropic.com/v1", "claude-opus-5-5", "console.anthropic.com", "旗舰 claude-fable-5-1", "labs"),
            P("gemini", "Google Gemini", "gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-3.1-pro", "aistudio.google.com", "最新 gemini-3.8-flash", "labs"),
            P("xai", "xAI Grok", "openai", "https://api.x.ai/v1", "grok-4.3", "console.x.ai", "2M 上下文；低价 grok-4.1-fast", "labs"),
            P("mistral", "Mistral AI", "openai", "https://api.mistral.ai/v1", "mistral-large-latest", "console.mistral.ai", "", "labs"),
            P("cohere", "Cohere", "openai", "https://api.cohere.ai/compatibility/v1", "command-a", "dashboard.cohere.com", "Compatibility endpoint", "labs"),
            P("perplexity", "Perplexity", "openai", "https://api.perplexity.ai", "sonar-pro", "perplexity.ai/settings/api", "Answers with live web citations", "labs"),
            P("azure", "Azure OpenAI", "openai", "https://YOUR-RESOURCE.openai.azure.com/openai/v1", "gpt-6.1-sol", "portal.azure.com", "先把 base 换成你的资源地址", "labs"),

            // aggregators & hosting
            P("openrouter", "OpenRouter", "openai", "https://openrouter.ai/api/v1", "anthropic/claude-sonnet-5.5", "openrouter.ai/keys", "One key, hundreds of models", "hosts"),
            P("groq", "Groq", "openai", "https://api.groq.com/openai/v1", "meta-llama/llama-4-scout-17b-16e-instruct", "console.groq.com", "Very fast inference", "hosts"),
            P("together", "Together AI", "openai", "https://api.together.xyz/v1", "meta-llama/Llama-4-Scout-17B-16E-Instruct", "api.together.xyz", "", "hosts"),
            P("fireworks", "Fireworks AI", "openai", "https://api.fireworks.ai/inference/v1", "accounts/fireworks/models/llama4-scout-instruct", "fireworks.ai", "", "hosts"),
            P("deepinfra", "DeepInfra", "openai", "https://api.deepinfra.com/v1/openai", "meta-llama/Llama-4-Scout-17B-16E-Instruct", "deepinfra.com", "", "hosts"),
            P("cerebras", "Cerebras", "openai", "https://api.cerebras.ai/v1", "llama-4-scout-17b-16e", "cloud.cerebras.ai", "", "hosts"),
            P("sambanova", "SambaNova", "openai", "https://api.sambanova.ai/v1", "Meta-Llama-4-Scout-17B-16E-Instruct", "cloud.sambanova.ai", "", "hosts"),
            P("nvidia", "NVIDIA NIM", "openai", "https://integrate.api.nvidia.com/v1", "meta/llama-3.3-70b-instruct", "ngc.nvidia.com", "", "hosts"),
            P("huggingface", "HuggingFace Inference", "openai", "https://router.huggingface.co/v1", "auto", "huggingface.co/settings/tokens", "auto 路由到最合适的开源模型", "hosts"),
            P("novita", "Novita AI", "openai", "https://api.novita.ai/v3/openai", "deepseek/deepseek-v4.1-flash", "novita.ai", "", "hosts"),
            P("hyperbolic", "Hyperbolic", "openai", "https://api.hyperbolic.xyz/v1", "meta-llama/Llama-4-Scout-17B-16E-Instruct", "app.hyperbolic.xyz", "", "hosts"),
            P("github", "GitHub Models", "openai", "https://models.github.ai/inference", "openai/gpt-6.1-sol", "github.com/settings/tokens", "用 GitHub token 免费试用各家模型", "hosts"),

            // china
            P("siliconflow", "SiliconFlow 硅基流动", "openai", "https://api.siliconflow.cn/v1", "deepseek-ai/DeepSeek-V4-Flash", "siliconflow.cn", "", "china"),
            P("infini", "无问芯穹 InfiniAI", "openai", "https://cloud.infini-ai.com/maas/v1", "deepseek-ai/DeepSeek-V4-Flash", "cloud.infini-ai.com", "", "china"),
            P("volcengine", "火山方舟 (Volcengine)", "openai", "https://ark.cn-beijing.volces.com/api/v3", "doubao-seed-evolving", "console.volcengine.com/ark", "统一别名，自动指向最新豆包", "china"),
            P("dashscope", "阿里云百炼 (Qwen)", "openai", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-max", "bailian.console.aliyun.com", "qwen-max 自动指向最新旗舰", "china"),
            P("zhipu", "智谱 GLM", "openai", "https://open.bigmodel.cn/api/paas/v4", "glm-5.3", "open.bigmodel.cn", "低价多模态 glm-5.3-flash", "china"),
            P("moonshot", "月之暗面 Kimi", "openai", "https://api.moonshot.cn/v1", "kimi-k2.6", "platform.moonshot.cn", "", "china"),
            P("minimax", "MiniMax", "openai", "https://api.minimaxi.com/v1", "MiniMax-M2", "platform.minimaxi.com", "", "china"),
            P("qianfan", "百度千帆 ERNIE", "openai", "https://qianfan.baidubce.com/v2", "ernie-5.0-tianque", "console.bce.baidu.com/qianfan", "", "china"),
            P("xfyun", "讯飞星火 Spark", "openai", "https://spark-api-open.xf-yun.com/v1", "Spark-X2.5", "xinghuo.xfyun.cn", "", "china"),
            P("hunyuan", "腾讯混元 Hunyuan", "openai", "https://api.hunyuan.cloud.tencent.com/v1", "hy3", "console.cloud.tencent.com/hunyuan", "", "china"),
            P("baichuan", "百川 Baichuan", "openai", "https://api.baichuan-ai.com/v1", "Baichuan4", "platform.baichuan-ai.com", "", "china"),
            P("stepfun", "阶跃星辰 StepFun", "openai", "https://api.stepfun.com/v1", "step-5-preview", "platform.stepfun.com", "", "china"),
            P("lingyi", "零一万物 Yi", "openai", "https://api.lingyiinac.com/v1", "yi-lightning", "platform.lingyiwanwu.com", "", "china"),
            P("sensenova", "商汤日日新 SenseNova", "openai", "https://api.sensenova.cn/compatible-mode/v1", "SenseChat-5", "platform.sensenova.cn", "", "china"),

            // local runtimes
            P("ollama", "Ollama (local)", "ollama", "http://127.0.0.1:11434", "qwen3:8b", "", "Runs on this machine, no key", "local"),
            P("lmstudio", "LM Studio (local)", "openai", "http://127.0.0.1:1234/v1", "local-model", "", "Start the local server first", "local"),
            P("vllm", "vLLM / llama.cpp (local)", "openai", "http://127.0.0.1:8000/v1", "my-model", "", "Any OpenAI-compatible server", "local"),

            // escape hatch
            P("custom", "Custom (OpenAI compatible)", "openai", "http://127.0.0.1:8000/v1", "my-model", "", "Point it anywhere", "custom"),
        };

        /// <summary>Suggestions for the model box, per protocol.</summary>
        public static readonly Dictionary<string, string[]> ModelHints = new Dictionary<string, string[]>
        {
            ["openai"] = new[]
            {
                "deepseek-chat", "deepseek-v4.1-flash", "deepseek-v4-pro",
                "gpt-6-astra", "gpt-6.1-sol", "gpt-6-luna", "gpt-5.6-sol",
                "claude-opus-5-5", "claude-sonnet-5-5", "claude-fable-5-1",
                "gemini-3.8-flash", "grok-4.3", "grok-4.1-fast",
                "mistral-large-latest", "command-a", "sonar-pro",
                "glm-5.3", "glm-5.3-flash", "kimi-k2.6", "qwen-max",
                "doubao-seed-evolving", "hy3", "ernie-5.0-tianque", "MiniMax-M2",
                "anthropic/claude-sonnet-5.5", "meta-llama/Llama-4-Scout-17B-16E-Instruct", "auto",
            },
            ["anthropic"] = new[] { "claude-fable-5-1", "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-4-5" },
            ["gemini"] = new[] { "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.1-pro", "gemini-3.1-flash" },
            ["ollama"] = new[] { "qwen3:8b", "deepseek-v4-flash:16b", "llama4:scout", "llama3.2", "glm-5:9b", "mistral", "phi4" },
        };

        public static ProviderPreset Find(string id)
        {
            foreach (ProviderPreset preset in All)
            {
                if (preset.Id == id)
                {
                    return preset;
                }
            }

            return All[0];
        }

        /// <summary>Turns a preset into a live provider profile, keeping any key already stored.</summary>
        public static ProviderProfile FromPreset(ProviderPreset preset, string keepKey)
        {
            return new ProviderProfile
            {
                Id = preset.Id,
                Name = preset.Name,
                Protocol = preset.Proto,
                BaseUrl = preset.Base,
                Model = preset.Model,
                ApiKey = keepKey ?? string.Empty,
                SystemPrompt = Persona.DefaultSys,
                Temperature = 0.7,
                MaxTokens = 0,
            };
        }

        /// <summary>The six that get a slot out of the box; everything else is one click away.</summary>
        public static List<ProviderProfile> DefaultProviders()
        {
            string[] featured = { "deepseek", "openai", "anthropic", "gemini", "openrouter", "ollama" };
            List<ProviderProfile> list = new List<ProviderProfile>();
            foreach (string id in featured)
            {
                list.Add(FromPreset(Find(id), string.Empty));
            }

            return list;
        }

        /// <summary>A new id for a provider the user added under a custom name.</summary>
        public static string CustomId(string taken)
        {
            string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            System.Random random = new System.Random();
            while (true)
            {
                System.Text.StringBuilder id = new System.Text.StringBuilder("custom-");
                for (int i = 0; i < 8; i++)
                {
                    id.Append(alphabet[random.Next(alphabet.Length)]);
                }

                string candidate = id.ToString();
                if (candidate != taken)
                {
                    return candidate;
                }
            }
        }
    }
}
