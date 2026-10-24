using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DomeOSAgentDev
{
    /// <summary>一张随消息一起发出去的图，base64 编码。</summary>
    internal sealed class WireImage
    {
        public string Mime { get; set; } = "image/png";

        public string Data { get; set; } = "";
    }

    /// <summary>模型要求执行的一次函数调用。</summary>
    internal sealed class ToolCall
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public JsonObject Args { get; set; } = new JsonObject();

        /// <summary>取一个字符串参数，缺失时是空串。</summary>
        public string Text(string key)
        {
            if (Args != null && Args.TryGetPropertyValue(key, out JsonNode? node) && node is JsonValue value)
            {
                if (value.TryGetValue(out string? text))
                {
                    return text ?? string.Empty;
                }

                return value.ToJsonString(Wire.Json).Trim('"');
            }

            return string.Empty;
        }

        public int Int(string key, int fallback)
        {
            if (Args != null && Args.TryGetPropertyValue(key, out JsonNode? node) && node is JsonValue value)
            {
                if (value.TryGetValue(out int number))
                {
                    return number;
                }

                if (value.TryGetValue(out double d))
                {
                    return (int)d;
                }
            }

            return fallback;
        }

        public bool Flag(string key)
        {
            if (Args != null && Args.TryGetPropertyValue(key, out JsonNode? node) && node is JsonValue value)
            {
                if (value.TryGetValue(out bool flag))
                {
                    return flag;
                }
            }

            return false;
        }
    }

    /// <summary>线路上的消息：四种协议都从这一种形状转换出去。</summary>
    internal sealed class WireMessage
    {
        public string Role { get; set; } = "user";

        public string Content { get; set; } = "";

        public List<WireImage> Images { get; } = new List<WireImage>();

        public List<ToolCall> ToolCalls { get; } = new List<ToolCall>();

        public string ToolCallId { get; set; } = "";

        public string Name { get; set; } = "";
    }

    /// <summary>一轮流式对话的结果。失败不会抛异常，而是回在这里。</summary>
    internal sealed class StreamResult
    {
        public bool Ok { get; set; }

        public string Text { get; set; } = "";

        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>已经本地化好的失败说明。</summary>
        public string Hint { get; set; } = "";

        public bool SawAnything { get; set; }
    }

    /// <summary>打包好的一个请求。</summary>
    internal sealed class ChatRequest
    {
        public string Url { get; set; } = "";

        public string SafeUrl { get; set; } = "";

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public string Body { get; set; } = "";
    }

    internal static class Wire
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    /// <summary>
    /// 与各家模型服务通话的部分：把消息编成对方要的形状，再把回来的 SSE 拆成增量文本和工具调用。
    /// </summary>
    internal static class ChatApi
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = true,
            });

            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        private static string TrimSlash(string url)
        {
            return (url ?? string.Empty).TrimEnd('/');
        }

        // ---------------------------------------------------------------- 请求体

        private static JsonArray OpenAiTools(List<ToolSpec> tools)
        {
            JsonArray array = new JsonArray();
            foreach (ToolSpec spec in tools)
            {
                array.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = spec.Name,
                        ["description"] = spec.Description,
                        ["parameters"] = spec.Parameters.DeepClone(),
                    },
                });
            }

            return array;
        }

        /// <summary>我们用的 JSON Schema 子集转成 Gemini 的声明形状。</summary>
        private static JsonObject ToGeminiSchema(JsonObject? schema)
        {
            if (schema == null)
            {
                return new JsonObject { ["type"] = "STRING" };
            }

            string raw = schema["type"]?.GetValue<string>() ?? "object";
            string type = raw switch
            {
                "string" => "STRING",
                "number" => "NUMBER",
                "integer" => "INTEGER",
                "boolean" => "BOOLEAN",
                "array" => "ARRAY",
                "object" => "OBJECT",
                _ => "STRING",
            };

            JsonObject output = new JsonObject { ["type"] = type };
            if (schema["description"] is JsonValue description)
            {
                output["description"] = description.GetValue<string>();
            }

            if (type == "OBJECT" && schema["properties"] is JsonObject properties)
            {
                JsonObject mapped = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> pair in properties)
                {
                    mapped[pair.Key] = ToGeminiSchema(pair.Value as JsonObject);
                }

                output["properties"] = mapped;
                if (schema["required"] is JsonArray required && required.Count > 0)
                {
                    output["required"] = required.DeepClone();
                }
            }

            if (type == "ARRAY" && schema["items"] is JsonObject items)
            {
                output["items"] = ToGeminiSchema(items);
            }

            return output;
        }

        public static ChatRequest Build(ProviderProfile provider, List<WireMessage> messages, List<ToolSpec> tools)
        {
            string baseUrl = TrimSlash(provider.BaseUrl);
            ChatRequest request = new ChatRequest();
            request.Headers["Content-Type"] = "application/json";

            List<WireMessage> convo = messages;
            bool hasSystem = false;
            foreach (WireMessage message in messages)
            {
                if (message.Role == "system")
                {
                    hasSystem = true;
                }
            }

            if (!hasSystem && !string.IsNullOrEmpty(provider.SystemPrompt))
            {
                convo = new List<WireMessage> { new WireMessage { Role = "system", Content = provider.SystemPrompt } };
                convo.AddRange(messages);
            }

            bool useTools = tools.Count > 0;

            if (provider.Protocol == "openai" || provider.Protocol == "ollama")
            {
                bool isOllama = provider.Protocol == "ollama";
                JsonObject body = new JsonObject
                {
                    ["model"] = provider.Model,
                    ["stream"] = true,
                };

                JsonArray wireMessages = new JsonArray();
                foreach (WireMessage message in convo)
                {
                    if (message.Role == "tool")
                    {
                        wireMessages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["content"] = message.Content,
                            ["tool_call_id"] = message.ToolCallId,
                        });
                        continue;
                    }

                    if (message.Role == "assistant" && message.ToolCalls.Count > 0)
                    {
                        JsonArray calls = new JsonArray();
                        foreach (ToolCall call in message.ToolCalls)
                        {
                            calls.Add(new JsonObject
                            {
                                ["id"] = call.Id,
                                ["type"] = "function",
                                ["function"] = new JsonObject
                                {
                                    ["name"] = call.Name,
                                    ["arguments"] = (call.Args ?? new JsonObject()).ToJsonString(Wire.Json),
                                },
                            });
                        }

                        wireMessages.Add(new JsonObject
                        {
                            ["role"] = "assistant",
                            ["content"] = message.Content ?? string.Empty,
                            ["tool_calls"] = calls,
                        });
                        continue;
                    }

                    if (isOllama)
                    {
                        JsonObject one = new JsonObject
                        {
                            ["role"] = message.Role,
                            ["content"] = message.Content ?? string.Empty,
                        };

                        if (message.Images.Count > 0)
                        {
                            JsonArray images = new JsonArray();
                            foreach (WireImage image in message.Images)
                            {
                                images.Add(image.Data);
                            }

                            one["images"] = images;
                        }

                        wireMessages.Add(one);
                        continue;
                    }

                    if (message.Images.Count == 0)
                    {
                        wireMessages.Add(new JsonObject
                        {
                            ["role"] = message.Role,
                            ["content"] = message.Content ?? string.Empty,
                        });
                        continue;
                    }

                    JsonArray parts = new JsonArray();
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        parts.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
                    }

                    foreach (WireImage image in message.Images)
                    {
                        parts.Add(new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = "data:" + image.Mime + ";base64," + image.Data,
                            },
                        });
                    }

                    wireMessages.Add(new JsonObject { ["role"] = message.Role, ["content"] = parts });
                }

                body["messages"] = wireMessages;

                if (isOllama)
                {
                    body["options"] = new JsonObject
                    {
                        ["temperature"] = provider.Temperature,
                        ["num_predict"] = provider.MaxTokens > 0 ? provider.MaxTokens : -1,
                    };

                    if (useTools)
                    {
                        body["tools"] = OpenAiTools(tools);
                    }
                }
                else
                {
                    body["temperature"] = provider.Temperature;
                    if (provider.MaxTokens > 0)
                    {
                        body["max_tokens"] = provider.MaxTokens;
                    }

                    if (useTools)
                    {
                        body["tools"] = OpenAiTools(tools);
                        body["tool_choice"] = "auto";
                    }
                }

                if (!string.IsNullOrEmpty(provider.ApiKey))
                {
                    request.Headers["Authorization"] = "Bearer " + provider.ApiKey;
                }

                request.Url = isOllama ? baseUrl + "/api/chat" : baseUrl + "/chat/completions";
                request.SafeUrl = request.Url;
                request.Body = body.ToJsonString(Wire.Json);
                return request;
            }

            if (provider.Protocol == "anthropic")
            {
                request.Headers["x-api-key"] = provider.ApiKey;
                request.Headers["anthropic-version"] = "2023-06-01";

                JsonArray anthropicMessages = new JsonArray();
                string system = string.Empty;

                foreach (WireMessage message in convo)
                {
                    if (message.Role == "system")
                    {
                        system = message.Content ?? string.Empty;
                        continue;
                    }

                    if (message.Role == "tool")
                    {
                        anthropicMessages.Add(new JsonObject
                        {
                            ["role"] = "user",
                            ["content"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["type"] = "tool_result",
                                    ["tool_use_id"] = message.ToolCallId,
                                    ["content"] = message.Content,
                                },
                            },
                        });
                        continue;
                    }

                    if (message.Role == "assistant" && message.ToolCalls.Count > 0)
                    {
                        JsonArray content = new JsonArray();
                        if (!string.IsNullOrEmpty(message.Content))
                        {
                            content.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
                        }

                        foreach (ToolCall call in message.ToolCalls)
                        {
                            content.Add(new JsonObject
                            {
                                ["type"] = "tool_use",
                                ["id"] = call.Id,
                                ["name"] = call.Name,
                                ["input"] = (call.Args ?? new JsonObject()).DeepClone(),
                            });
                        }

                        anthropicMessages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                        continue;
                    }

                    if (message.Images.Count == 0)
                    {
                        anthropicMessages.Add(new JsonObject
                        {
                            ["role"] = message.Role,
                            ["content"] = message.Content ?? string.Empty,
                        });
                        continue;
                    }

                    JsonArray parts = new JsonArray();
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        parts.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
                    }

                    foreach (WireImage image in message.Images)
                    {
                        parts.Add(new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject
                            {
                                ["type"] = "base64",
                                ["media_type"] = image.Mime,
                                ["data"] = image.Data,
                            },
                        });
                    }

                    anthropicMessages.Add(new JsonObject { ["role"] = message.Role, ["content"] = parts });
                }

                JsonObject body = new JsonObject
                {
                    ["model"] = provider.Model,
                    ["max_tokens"] = provider.MaxTokens > 0 ? provider.MaxTokens : 8192,
                    ["temperature"] = provider.Temperature,
                    ["stream"] = true,
                    ["messages"] = anthropicMessages,
                };

                if (!string.IsNullOrEmpty(system))
                {
                    body["system"] = system;
                }

                if (useTools)
                {
                    JsonArray declarations = new JsonArray();
                    foreach (ToolSpec spec in tools)
                    {
                        declarations.Add(new JsonObject
                        {
                            ["name"] = spec.Name,
                            ["description"] = spec.Description,
                            ["input_schema"] = spec.Parameters.DeepClone(),
                        });
                    }

                    body["tools"] = declarations;
                }

                request.Url = baseUrl + "/messages";
                request.SafeUrl = request.Url;
                request.Body = body.ToJsonString(Wire.Json);
                return request;
            }

            // gemini
            JsonArray contents = new JsonArray();
            string geminiSystem = string.Empty;

            foreach (WireMessage message in convo)
            {
                if (message.Role == "system")
                {
                    geminiSystem = message.Content ?? string.Empty;
                    continue;
                }

                if (message.Role == "tool")
                {
                    contents.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["functionResponse"] = new JsonObject
                                {
                                    ["name"] = string.IsNullOrEmpty(message.Name) ? "tool" : message.Name,
                                    ["response"] = new JsonObject { ["result"] = message.Content },
                                },
                            },
                        },
                    });
                    continue;
                }

                if (message.Role == "assistant" && message.ToolCalls.Count > 0)
                {
                    JsonArray callParts = new JsonArray();
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        callParts.Add(new JsonObject { ["text"] = message.Content });
                    }

                    foreach (ToolCall call in message.ToolCalls)
                    {
                        callParts.Add(new JsonObject
                        {
                            ["functionCall"] = new JsonObject
                            {
                                ["name"] = call.Name,
                                ["args"] = (call.Args ?? new JsonObject()).DeepClone(),
                            },
                        });
                    }

                    contents.Add(new JsonObject { ["role"] = "model", ["parts"] = callParts });
                    continue;
                }

                JsonArray gParts = new JsonArray();
                if (!string.IsNullOrEmpty(message.Content))
                {
                    gParts.Add(new JsonObject { ["text"] = message.Content });
                }

                foreach (WireImage image in message.Images)
                {
                    gParts.Add(new JsonObject
                    {
                        ["inline_data"] = new JsonObject
                        {
                            ["mime_type"] = image.Mime,
                            ["data"] = image.Data,
                        },
                    });
                }

                contents.Add(new JsonObject
                {
                    ["role"] = message.Role == "assistant" ? "model" : "user",
                    ["parts"] = gParts,
                });
            }

            JsonObject geminiBody = new JsonObject
            {
                ["contents"] = contents,
                ["generationConfig"] = new JsonObject
                {
                    ["temperature"] = provider.Temperature,
                },
            };

            if (provider.MaxTokens > 0)
            {
                ((JsonObject)geminiBody["generationConfig"]!)["maxOutputTokens"] = provider.MaxTokens;
            }

            if (!string.IsNullOrEmpty(geminiSystem))
            {
                geminiBody["systemInstruction"] = new JsonObject
                {
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = geminiSystem } },
                };
            }

            if (useTools)
            {
                JsonArray declarations = new JsonArray();
                foreach (ToolSpec spec in tools)
                {
                    declarations.Add(new JsonObject
                    {
                        ["name"] = spec.Name,
                        ["description"] = spec.Description,
                        ["parameters"] = ToGeminiSchema(spec.Parameters),
                    });
                }

                geminiBody["tools"] = new JsonArray
                {
                    new JsonObject { ["functionDeclarations"] = declarations },
                };
            }

            request.Url = baseUrl + "/models/" + Uri.EscapeDataString(provider.Model)
                          + ":streamGenerateContent?alt=sse&key=" + Uri.EscapeDataString(provider.ApiKey);
            request.SafeUrl = baseUrl + "/models/" + Uri.EscapeDataString(provider.Model) + ":streamGenerateContent";
            request.Body = geminiBody.ToJsonString(Wire.Json);
            return request;
        }

        // ---------------------------------------------------------------- SSE 累积

        internal sealed class Accumulator
        {
            private readonly string _protocol;
            private readonly Action<string>? _onDelta;
            private readonly StringBuilder _text = new StringBuilder();
            private readonly List<ToolCall> _calls = new List<ToolCall>();
            private readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
            private int _anonymous;

            private sealed class Pending
            {
                public string Id = "";

                public string Name = "";

                public StringBuilder Json = new StringBuilder();
            }

            public bool SawAnything { get; private set; }

            public Accumulator(string protocol, Action<string>? onDelta)
            {
                _protocol = protocol;
                _onDelta = onDelta;
            }

            private static string NewId()
            {
                return "call_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }

            private void AppendText(string piece)
            {
                _text.Append(piece);
                _onDelta?.Invoke(piece);
            }

            public void Feed(string line)
            {
                string trimmed = (line ?? string.Empty).Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(":", StringComparison.Ordinal))
                {
                    return;
                }

                if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(5).Trim();
                }

                if (trimmed.Length == 0 || trimmed == "[DONE]")
                {
                    return;
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(trimmed);
                }
                catch
                {
                    return;
                }

                if (node is not JsonObject json)
                {
                    return;
                }

                SawAnything = true;

                if (_protocol == "anthropic")
                {
                    FeedAnthropic(json);
                    return;
                }

                if (_protocol == "gemini")
                {
                    FeedGemini(json);
                    return;
                }

                if (_protocol == "ollama")
                {
                    FeedOllama(json);
                    return;
                }

                FeedOpenAi(json);
            }

            private void FeedAnthropic(JsonObject json)
            {
                string type = json["type"]?.GetValue<string>() ?? string.Empty;
                if (type == "content_block_start")
                {
                    if (json["content_block"] is not JsonObject block)
                    {
                        return;
                    }

                    if (block["type"]?.GetValue<string>() == "tool_use")
                    {
                        int index = json["index"]?.GetValue<int>() ?? 0;
                        _pending[index] = new Pending
                        {
                            Id = string.IsNullOrEmpty(block["id"]?.GetValue<string>())
                                ? NewId()
                                : block["id"]!.GetValue<string>(),
                            Name = block["name"]?.GetValue<string>() ?? string.Empty,
                        };
                    }
                    else if (block["text"]?.GetValue<string>() is string text && text.Length > 0)
                    {
                        AppendText(text);
                    }

                    return;
                }

                if (type == "content_block_delta" && json["delta"] is JsonObject delta)
                {
                    string kind = delta["type"]?.GetValue<string>() ?? string.Empty;
                    if (kind == "text_delta" && delta["text"]?.GetValue<string>() is string piece && piece.Length > 0)
                    {
                        AppendText(piece);
                    }
                    else if (kind == "input_json_delta" && delta["partial_json"]?.GetValue<string>() is string part)
                    {
                        int index = json["index"]?.GetValue<int>() ?? 0;
                        if (_pending.TryGetValue(index, out Pending? slot))
                        {
                            slot.Json.Append(part);
                        }
                    }
                }
            }

            private void FeedGemini(JsonObject json)
            {
                if (json["candidates"] is not JsonArray candidates || candidates.Count == 0)
                {
                    return;
                }

                if (candidates[0] is not JsonObject candidate
                    || candidate["content"] is not JsonObject content
                    || content["parts"] is not JsonArray parts)
                {
                    return;
                }

                foreach (JsonNode? item in parts)
                {
                    if (item is not JsonObject part)
                    {
                        continue;
                    }

                    if (part["thought"] is JsonValue thought && thought.TryGetValue(out bool isThought) && isThought)
                    {
                        continue;
                    }

                    if (part["text"]?.GetValue<string>() is string text && text.Length > 0)
                    {
                        AppendText(text);
                    }

                    if (part["functionCall"] is JsonObject function)
                    {
                        ToolCall call = new ToolCall
                        {
                            Name = function["name"]?.GetValue<string>() ?? string.Empty,
                        };

                        call.Id = string.IsNullOrEmpty(function["id"]?.GetValue<string>())
                            ? NewId()
                            : function["id"]!.GetValue<string>();

                        if (function["args"] is JsonObject args)
                        {
                            call.Args = (JsonObject)args.DeepClone();
                        }

                        _calls.Add(call);
                    }
                }
            }

            private void FeedOllama(JsonObject json)
            {
                if (json["message"] is not JsonObject message)
                {
                    return;
                }

                if (message["content"]?.GetValue<string>() is string text && text.Length > 0)
                {
                    AppendText(text);
                }

                if (message["tool_calls"] is not JsonArray calls)
                {
                    return;
                }

                foreach (JsonNode? item in calls)
                {
                    if (item is not JsonObject one || one["function"] is not JsonObject function)
                    {
                        continue;
                    }

                    ToolCall call = new ToolCall
                    {
                        Id = string.IsNullOrEmpty(one["id"]?.GetValue<string>()) ? NewId() : one["id"]!.GetValue<string>(),
                        Name = function["name"]?.GetValue<string>() ?? string.Empty,
                    };

                    if (function["arguments"] is JsonObject args)
                    {
                        call.Args = (JsonObject)args.DeepClone();
                    }
                    else if (function["arguments"]?.GetValue<string>() is string raw)
                    {
                        call.Args = ParseArgs(raw);
                    }

                    _calls.Add(call);
                }
            }

            private void FeedOpenAi(JsonObject json)
            {
                if (json["choices"] is not JsonArray choices || choices.Count == 0)
                {
                    return;
                }

                if (choices[0] is not JsonObject choice)
                {
                    return;
                }

                JsonObject delta = choice["delta"] as JsonObject ?? choice["message"] as JsonObject ?? new JsonObject();

                if (delta["content"]?.GetValue<string>() is string text && text.Length > 0)
                {
                    AppendText(text);
                }

                if (delta["tool_calls"] is not JsonArray list)
                {
                    return;
                }

                foreach (JsonNode? item in list)
                {
                    if (item is not JsonObject call)
                    {
                        continue;
                    }

                    int index = call["index"] is JsonValue indexValue && indexValue.TryGetValue(out int number)
                        ? number
                        : _anonymous++;

                    if (!_pending.TryGetValue(index, out Pending? slot))
                    {
                        slot = new Pending();
                        _pending[index] = slot;
                    }

                    string? id = call["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(id))
                    {
                        slot.Id = id!;
                    }

                    JsonObject? function = call["function"] as JsonObject;
                    string? name = function?["name"]?.GetValue<string>() ?? call["name"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name))
                    {
                        slot.Name = name!;
                    }

                    string? arguments = function?["arguments"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(arguments))
                    {
                        slot.Json.Append(arguments);
                    }
                }
            }

            private static JsonObject ParseArgs(string raw)
            {
                string text = (raw ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return new JsonObject();
                }

                try
                {
                    if (JsonNode.Parse(text) is JsonObject parsed)
                    {
                        return parsed;
                    }
                }
                catch
                {
                    // 不是合法 JSON 时原样交给模型，让它自己纠正
                }

                return new JsonObject { ["_raw"] = text };
            }

            public StreamResult Finish()
            {
                foreach (Pending slot in _pending.Values)
                {
                    if (string.IsNullOrEmpty(slot.Name))
                    {
                        continue;
                    }

                    _calls.Add(new ToolCall
                    {
                        Id = string.IsNullOrEmpty(slot.Id) ? NewId() : slot.Id,
                        Name = slot.Name,
                        Args = ParseArgs(slot.Json.ToString()),
                    });
                }

                return new StreamResult
                {
                    Ok = true,
                    Text = _text.ToString(),
                    ToolCalls = _calls,
                    SawAnything = SawAnything,
                };
            }
        }

        // ---------------------------------------------------------------- 一轮请求

        private static string ExtractErrorMessage(string raw)
        {
            string whole = TryMessage(raw.Trim());
            if (whole.Length > 0)
            {
                return whole;
            }

            foreach (string line in raw.Split('\n'))
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith(":", StringComparison.Ordinal))
                {
                    continue;
                }

                string payload = text.StartsWith("data:", StringComparison.Ordinal) ? text.Substring(5).Trim() : text;
                if (payload.Length == 0 || payload == "[DONE]")
                {
                    continue;
                }

                string found = TryMessage(payload);
                if (found.Length > 0)
                {
                    return found;
                }
            }

            return string.Empty;
        }

        private static string TryMessage(string raw)
        {
            try
            {
                if (JsonNode.Parse(raw) is JsonObject json)
                {
                    string? text = json["error"]?["message"]?.GetValue<string>()
                                   ?? json["error"]?["type"]?.GetValue<string>()
                                   ?? json["message"]?.GetValue<string>()
                                   ?? json["detail"]?.GetValue<string>();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text!.Trim();
                    }
                }
            }
            catch
            {
                // 不是 JSON，交给调用方
            }

            return string.Empty;
        }

        private static string HintForStatus(int status)
        {
            return status switch
            {
                400 => "请求被服务商判为不合法（400）",
                401 => "密钥无效或未授权（401），去「设置 → 服务商」换一个 Key",
                402 => "账户余额不足（402）",
                403 => "该 Key 没有访问这个模型的权限（403）",
                404 => "地址或模型名不对（404），检查 Base URL 与模型名",
                413 => "这一轮的上下文太长（413）",
                429 => "触发限流或额度用尽（429），缓一下再试",
                _ => status >= 500 ? "服务商那边出错了（HTTP " + status + "）" : "请求失败（HTTP " + status + "）",
            };
        }

        /// <summary>跑一轮流式对话。绝不抛异常，失败以 Ok=false 返回。</summary>
        public static async Task<StreamResult> StreamAsync(
            ProviderProfile provider,
            List<WireMessage> messages,
            List<ToolSpec> tools,
            Action<string>? onDelta,
            Action<string>? onStatus,
            CancellationToken token)
        {
            ChatRequest request;
            try
            {
                request = Build(provider, messages, tools);
            }
            catch (Exception error)
            {
                return new StreamResult { Ok = false, Hint = "组装请求失败：" + error.Message };
            }

            onStatus?.Invoke("POST " + request.SafeUrl);

            HttpResponseMessage response;
            try
            {
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, request.Url);
                foreach (KeyValuePair<string, string> header in request.Headers)
                {
                    if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

                response = await Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            }
            catch (OperationCanceledException)
            {
                return new StreamResult { Ok = true };
            }
            catch (Exception error)
            {
                return new StreamResult { Ok = false, Hint = "连不上 " + provider.Name + "：" + error.Message };
            }

            if (!response.IsSuccessStatusCode)
            {
                string detail = string.Empty;
                try
                {
                    detail = ExtractErrorMessage(await response.Content.ReadAsStringAsync());
                }
                catch
                {
                    // 读不到详情就只看状态码
                }

                if (detail.Length > 900)
                {
                    detail = detail.Substring(0, 900) + "...";
                }

                int status = (int)response.StatusCode;
                string hint = HintForStatus(status) + " " + status;
                if (detail.Length > 0)
                {
                    hint += " · " + detail;
                }

                response.Dispose();
                return new StreamResult { Ok = false, Hint = hint };
            }

            Accumulator accumulator = new Accumulator(provider.Protocol, onDelta);
            try
            {
                await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(token);
                using System.IO.StreamReader reader = new System.IO.StreamReader(stream, Encoding.UTF8);
                while (true)
                {
                    string? line = await reader.ReadLineAsync(token);
                    if (line == null)
                    {
                        break;
                    }

                    accumulator.Feed(line);
                }
            }
            catch (OperationCanceledException)
            {
                return accumulator.Finish();
            }
            catch (Exception error)
            {
                StreamResult partial = accumulator.Finish();
                partial.Ok = false;
                partial.Hint = "传输中断：" + error.Message;
                return partial;
            }
            finally
            {
                response.Dispose();
            }

            StreamResult result = accumulator.Finish();
            if (result.Text.Length == 0 && result.ToolCalls.Count == 0 && !accumulator.SawAnything)
            {
                result.Ok = false;
                result.Hint = "服务商返回了空响应";
            }

            return result;
        }

        /// <summary>尽力而为的模型列表，给「拉取模型」用。</summary>
        public static async Task<List<string>> ListModelsAsync(ProviderProfile provider, CancellationToken token)
        {
            List<string> models = new List<string>();
            string baseUrl = TrimSlash(provider.BaseUrl);

            try
            {
                string url;
                if (provider.Protocol == "ollama")
                {
                    url = baseUrl + "/api/tags";
                }
                else if (provider.Protocol == "gemini")
                {
                    url = baseUrl + "/models?key=" + Uri.EscapeDataString(provider.ApiKey);
                }
                else
                {
                    url = baseUrl + "/models";
                }

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
                if (provider.Protocol == "openai" && !string.IsNullOrEmpty(provider.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + provider.ApiKey);
                }

                using HttpResponseMessage response = await Client.SendAsync(request, token);
                if (!response.IsSuccessStatusCode)
                {
                    return models;
                }

                if (JsonNode.Parse(await response.Content.ReadAsStringAsync()) is not JsonObject json)
                {
                    return models;
                }

                // 有的服务商把两份清单都塞进同一个响应里，而且内容还不一样——OpenAI 标准的 data
                // 才是完整的那份，所以先读 data，再把 models 里多出来的补上。
                Collect(json["data"] as JsonArray, models);
                Collect(json["models"] as JsonArray, models);
            }
            catch
            {
                // 离线也没关系
            }

            return models;
        }

        /// <summary>从一串模型对象里挑出名字，id / name / model 三种写法都认。</summary>
        private static void Collect(JsonArray? list, List<string> models)
        {
            if (list == null)
            {
                return;
            }

            foreach (JsonNode? item in list)
            {
                if (item is not JsonObject one)
                {
                    continue;
                }

                string? name = one["id"]?.GetValue<string>()
                               ?? one["name"]?.GetValue<string>()
                               ?? one["model"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string clean = name!.StartsWith("models/", StringComparison.Ordinal)
                    ? name.Substring("models/".Length)
                    : name;

                if (!models.Contains(clean, StringComparer.OrdinalIgnoreCase))
                {
                    models.Add(clean);
                }
            }
        }
    }
}
